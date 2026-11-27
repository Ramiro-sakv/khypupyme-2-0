using System.Security.Cryptography;
using System.Text.Json;
using KhypuPyme.Api.Businesses;
using KhypuPyme.Api.Billing;
using KhypuPyme.Api.Persistence;
using KhypuPyme.Api.Timber;
using KhypuPyme.Api.Retail;
using KhypuPyme.Api.Services;
using KhypuPyme.Api.Security;
using Microsoft.EntityFrameworkCore;
using static KhypuPyme.Api.Commerce.CommerceRules;

// Recibe los comandos comerciales, verifica el contexto del negocio y elige qué operación se ejecuta. Guarda la operación y su recibo juntos.
namespace KhypuPyme.Api.Commerce;

internal sealed class CommerceService(KhypuPymeDbContext db, BusinessAccess access, LicenseAccess license, SecurityProofs proofs, TimeProvider clock)
{
    public async Task<CommandResult> ExecuteAsync(Guid accountId, Guid businessId, CommerceCommand command, bool practiceChannel, CancellationToken ct)
    {
        // El identificador permite reconocer una solicitud repetida.
        if (command.OperationId == Guid.Empty || command.Kind is null or { Length: > 30 })
            throw new CommerceFailure("La solicitud comercial no es válida.");

        // Cada tipo de operación admite solo los datos que necesita.
        if ((command.Kind == "configure-business") != (command.Policy is not null))
            throw new CommerceFailure("Las reglas de operación solo corresponden a la configuración del negocio.");
        if (command.Containers is not null && command.Kind is not ("sale" or "containers"))
            throw new CommerceFailure("Registra los envases en una venta o en su movimiento independiente.");
        if (command.Variants is not null && command.Kind != "variants")
            throw new CommerceFailure("La lista de variantes solo corresponde al alta conjunta de variantes.");
        if (command.ExchangeLines?.Any(x => x?.TimberRateManual is not null) == true ||
            command.Lines?.Any(x => x?.TimberRateManual is not null &&
                (x.TimberRateManual != true || x.Timber is null || command.Kind is not ("sale" or "quote"))) == true)
            throw new CommerceFailure("La tarifa manual solo corresponde a madera en una venta o cotización.");
        if (command.ExchangeLines?.Any(x => x?.TimberCostUnit is not null) == true ||
            command.Lines?.Any(x => x?.TimberCostUnit is not null &&
                (x.TimberCostUnit != "board-foot" || x.Timber is null || command.Kind is not ("purchase" or "stock-in"))) == true)
            throw new CommerceFailure("El costo por pie tablar corresponde a una entrada o compra de madera.");
        if ((command.WorkLink is not null && command.Kind is not ("save-work" or "advance-work" or "repair-receipt" or "work-receipt" or "sale")) ||
            (command.Work is not null && command.Kind != "save-work") || (command.WorkState is not null && command.Kind != "advance-work") ||
            (command.ServiceProduct is not null && command.Kind != "product"))
            throw new CommerceFailure("Los datos de la cita o servicio no corresponden a la operación.");

        // Abre la transacción y establece en PostgreSQL la cuenta y el negocio de esta solicitud.
        await using var transaction = await access.OpenAsync(accountId, businessId, ct);
        // La consulta distingue el negocio habitual de su espacio de práctica.
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == businessId &&
            (x.PracticeOriginId != null) == practiceChannel, ct)
            ?? throw new CommerceFailure("No se encontró el negocio.", 404);

        // Primero se bloquea la pareja cuenta + solicitud para detectar envíos duplicados.
        var requestKey = accountId.ToString("N") + command.OperationId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({requestKey}, 4))", ct);
        // La huella identifica el contenido original asociado a ese identificador.
        var digest = Digest(command);
        var receipt = await db.Set<CommerceReceipt>().FindAsync([accountId, command.OperationId], ct);
        if (receipt is not null)
        {
            // El mismo identificador debe conservar el negocio y los datos originales.
            if (receipt.BusinessId != businessId || receipt.Digest != digest)
                throw new CommerceFailure("Ese identificador ya se utilizó con otra solicitud.", 409);
            // Devuelve el resultado guardado si el cliente envió de nuevo la misma venta.
            return new CommandResult(receipt.Id, receipt.Kind, receipt.EntityId);
        }

        // Este bloqueo dura hasta que termine la transacción. Si dos cajas venden la última pieza,
        // la segunda espera y comprueba el saldo después de que se haya confirmado la primera.
        await access.LockBusinessAsync(businessId, ct);
        if (await access.ClosedAsync(accountId, ct))
            throw new CommerceFailure("La cuenta tiene una solicitud de eliminación.", 403);
        if (await access.DeletingAsync(business.PracticeOriginId ?? businessId, ct))
            throw new CommerceFailure("El negocio tiene una limpieza pendiente.", 409);

        // Toda la operación comparte el mismo instante de referencia para evaluar la licencia y fechar los registros.
        var now = clock.GetUtcNow();
        // El cierre de una Caja existente también se permite con la licencia vencida.
        if (command.Kind != "close-cash")
            await license.RequireAsync(accountId, business.PracticeOriginId ?? businessId, now, ct);
        var practice = practiceChannel
            ? await db.Set<TimberPractice>().SingleOrDefaultAsync(x => x.SandboxId == businessId, ct)
            : null;
        if (practiceChannel && practice?.State != "active")
            throw new CommerceFailure("Reanuda la práctica antes de registrar movimientos.", 409);
        if (practiceChannel && command.Kind is ("stock-in" or "cash-adjust" or "manual-debt" or "void-debt" or "configure-business"))
            throw new CommerceFailure("Ese ajuste manual no forma parte de esta práctica.", 403);
        // Los módulos comerciales comparten el mismo contexto de escritura y transacción.
        var work = new CommerceTransaction(db, business, command, now, ct);
        SecurityProof? approval = null;
        object? previous = null;
        if (!practiceChannel && (CorrectionAudit.Protected(command) || await CorrectionAudit.CashDifferenceAsync(db, command, ct)))
        {
            // Las correcciones protegidas consumen una aprobación ligada a esta solicitud.
            approval = await proofs.FindAsync(accountId, command.OperationId, "correction", businessId, digest, ct);
            proofs.Consume(approval);
            previous = await CorrectionAudit.BeforeAsync(work);
        }

        // Delega según el tipo enviado por el cliente y conserva el ID del registro creado o afectado.
        var entityId = command.Kind switch
        {
            "configure-business" => await BusinessPolicy.UpdateAsync(work),
            "product" => await SaveProductAsync(work),
            "variants" => await SaveVariantsAsync(work),
            "customer" => await SaveCustomerAsync(work),
            "purchase" => await new TradeOperations(work).PurchaseAsync(),
            "stock-in" => await new TradeOperations(work).StockInAsync(),
            "sale" or "quote" => await new TradeOperations(work).SellAsync(),
            "return" or "exchange" => await new TradeOperations(work).ReturnAsync(),
            "inspection" => await new EquipmentInventory(work).InspectAsync(),
            "warranty" => await new EquipmentInventory(work).WarrantyAsync(),
            "containers" => await new ContainerService(work).RegisterAsync(),
            "container-stock" => await new ContainerService(work).ReceiveStockAsync(),
            "open-cement" => await new CementOperations(work).OpenAsync(),
            "schedule-delivery" or "prepare-delivery" or "complete-delivery" or "cancel-delivery" or "delivery-receipt" =>
                await new FurnitureDeliveries(work).ExecuteAsync(),
            "save-work" => await new WorkOperations(work).SaveAsync(),
            "advance-work" => await new WorkOperations(work).TransitionAsync(),
            "repair-receipt" => await new WorkOperations(work).ReceiveAsync(),
            "work-receipt" => await new WorkOperations(work).IssueReceiptAsync(),
            "hold-lot" or "release-lot" or "discard-lot" => await new RetailInventory(work).CorrectLotAsync(),
            "stock-loss" => await new TradeOperations(work).LossAsync(),
            "open-cash" or "close-cash" or "cash-adjust" or "expense" or "void-expense" or "collect" or "manual-debt" or "void-debt"
                => await new CashOperations(work).ExecuteAsync(),
            _ => throw new CommerceFailure("El tipo de operación no es válido."),
        };

        if (practice is not null) PracticeSteps.Advance(practice, work);
        if (approval is not null) CorrectionAudit.Record(work, accountId, entityId, approval.Reason, previous);

        // Este registro permite consultar el resultado incluso si el cliente perdió la respuesta.
        db.Add(new CommerceReceipt
        {
            Id = command.OperationId,
            AccountId = accountId,
            BusinessId = businessId,
            Digest = digest,
            Kind = command.Kind,
            EntityId = entityId,
        });

        // SaveChangesAsync envía a PostgreSQL los cambios de existencias, Caja, comprobante y recibo.
        await db.SaveChangesAsync(ct);
        // CommitAsync confirma todos esos cambios juntos. Si algo falla antes de confirmar, se revierte la transacción.
        await transaction.CommitAsync(ct);
        return new CommandResult(command.OperationId, command.Kind, entityId);
    }

    // Resume el contenido de la solicitud en una huella. Sirve para detectar si alguien vuelve a usar el mismo identificador con otro contenido.
    internal static string Digest(CommerceCommand command)
    {
        // Los contratos anteriores mantienen sus huellas, incluso cuando se agregan campos opcionales.
        bool specialized = command.RetailProduct is not null || command.ApparelProduct is not null || command.EquipmentProduct is not null ||
            command.ExchangeLines is not null || command.Serial is not null || command.Release is not null ||
            command.Lines?.Any(x => x?.Measure is not null || x?.Serials is not null) == true;
        var timber = command.TimberProduct is not null || command.Lines?.Any(x => x?.Timber is not null || x?.OriginalLineId is not null) == true;
        bool extended = command.CosmeticProduct is not null || command.Containers is not null || command.StockQuantity is not null ||
            command.Lines?.Any(x => x?.Measure?.Loose is not null) == true;
        var current = command.DetailProduct is not null || command.PartsProduct is not null || command.PrintingProduct is not null ||
            command.RetailProduct?.RollLength is not null ||
            command.Lines?.Any(x => x?.Printing is not null) == true ||
            command.ExchangeLines?.Any(x => x?.Printing is not null) == true;
        object? oldRetail = command.RetailProduct is { } retail ? new { retail.Unit, retail.BoxSize, retail.TrackLots, retail.Reorder } : null;
        var material = command.FurnitureProduct is not null || command.TileProduct is not null || command.CementProduct is not null ||
            command.Delivery is not null || command.Lines?.Any(x => x?.Tile is not null) == true ||
            command.ExchangeLines?.Any(x => x?.Tile is not null) == true;
        var service = command.ServiceProduct is not null || command.Work is not null || command.WorkLink is not null || command.WorkState is not null;
        var negotiated = command.Lines?.Any(x => x?.TimberRateManual == true || x?.TimberCostUnit is not null) == true;
        object serialized = service || negotiated || command.Variants is not null || command.Policy is not null ? command : material
            ? new
            {
                command.OperationId, command.Kind, command.EntityId, command.CustomerId, command.SourceId, command.Name,
                command.Code, command.Phone, command.Method, command.Note, command.AmountMinor, command.DiscountMinor, command.Active,
                command.Lines, command.TimberProduct, command.RetailProduct, command.ApparelProduct, command.EquipmentProduct,
                command.ExchangeLines, command.Serial, command.Release, command.CosmeticProduct, command.Containers, command.StockQuantity,
                command.DetailProduct, command.PartsProduct, command.PrintingProduct,
                command.FurnitureProduct, command.TileProduct, command.CementProduct, command.Delivery,
            }
            : current
            ? new
            {
                command.OperationId, command.Kind, command.EntityId, command.CustomerId, command.SourceId, command.Name,
                command.Code, command.Phone, command.Method, command.Note, command.AmountMinor, command.DiscountMinor, command.Active,
                Lines = command.Lines?.Select(SpecialtyLine), command.TimberProduct, command.RetailProduct, command.ApparelProduct,
                command.EquipmentProduct, ExchangeLines = command.ExchangeLines?.Select(SpecialtyLine), command.Serial, command.Release,
                command.CosmeticProduct, command.Containers, command.StockQuantity, command.DetailProduct, command.PartsProduct, command.PrintingProduct,
            }
            : extended
            ? new
            {
                command.OperationId, command.Kind, command.EntityId, command.CustomerId, command.SourceId, command.Name,
                command.Code, command.Phone, command.Method, command.Note, command.AmountMinor, command.DiscountMinor, command.Active,
                Lines = command.Lines?.Select(ExtendedLine), command.TimberProduct, RetailProduct = oldRetail, command.ApparelProduct,
                command.EquipmentProduct, ExchangeLines = command.ExchangeLines?.Select(ExtendedLine), command.Serial, command.Release,
                command.CosmeticProduct, command.Containers, command.StockQuantity,
            }
            : specialized
            ? new
            {
                command.OperationId, command.Kind, command.EntityId, command.CustomerId, command.SourceId, command.Name,
                command.Code, command.Phone, command.Method, command.Note, command.AmountMinor, command.DiscountMinor, command.Active,
                Lines = command.Lines?.Select(PreviousLine), command.TimberProduct, RetailProduct = oldRetail, command.ApparelProduct,
                command.EquipmentProduct, ExchangeLines = command.ExchangeLines?.Select(PreviousLine), command.Serial, command.Release,
            }
            : timber
            ? new
            {
                command.OperationId, command.Kind, command.EntityId, command.CustomerId, command.SourceId, command.Name,
                command.Code, command.Phone, command.Method, command.Note, command.AmountMinor, command.DiscountMinor, command.Active,
                Lines = command.Lines?.Select(x => x is null ? null : new { x.ProductId, x.Quantity, x.UnitMinor, x.Restock, x.Timber, x.OriginalLineId }),
                command.TimberProduct,
            }
            : new
            {
                command.OperationId, command.Kind, command.EntityId, command.CustomerId, command.SourceId, command.Name,
                command.Code, command.Phone, command.Method, command.Note, command.AmountMinor, command.DiscountMinor,
                command.Active, Lines = command.Lines?.Select(x => x is null ? null : new { x.ProductId, x.Quantity, x.UnitMinor, x.Restock }),
            };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(serialized)));

        static object? PreviousLine(CommandLine? line) => line is null ? null : new
        {
            line.ProductId, line.Quantity, line.UnitMinor, line.Restock, line.Timber, line.OriginalLineId,
            Measure = line.Measure is { } m ? new { m.Quantity, m.Unit, m.Lot, m.ExpiresOn } : null,
            line.Serials,
        };
        static object? ExtendedLine(CommandLine? line) => line is null ? null : new
        {
            line.ProductId, line.Quantity, line.UnitMinor, line.Restock, line.Timber, line.OriginalLineId, line.Measure, line.Serials,
        };
        static object? SpecialtyLine(CommandLine? line) => line is null ? null : new
        {
            line.ProductId, line.Quantity, line.UnitMinor, line.Restock, line.Timber, line.OriginalLineId, line.Measure, line.Serials, line.Printing,
        };
    }

    // Agrupa el alta de variantes del modelo en la operación actual. Cada ficha conserva sus datos propios de talla, color y código.
    private static async Task<Guid> SaveVariantsAsync(CommerceTransaction work)
    {
        var input = work.Command;
        if (!SpecialtyProfiles.HasVariants(work.Business.Industry) || input.Variants is not { Length: >= 1 and <= 20 } variants)
            throw new CommerceFailure("Registra de 1 a 20 variantes juntas en ropa o calzado.");
        var name = Text(input.Name, 100, "el nombre del producto", true);
        var model = Text(input.Code, 80, "el modelo", true);
        var ids = variants.Where(variant => variant is not null).Select(variant => variant.Id).ToHashSet();
        if (ids.Count != variants.Length || ids.Contains(Guid.Empty) || await work.Db.Set<Product>().AnyAsync(item => ids.Contains(item.Id), work.Ct))
            throw new CommerceFailure("Cada variante debe tener un identificador nuevo y distinto.", 409);
        foreach (var variant in variants.OrderBy(x => string.IsNullOrWhiteSpace(x.Code)))
        {
            var product = new CommerceCommand(variant.Id, "product", Name: name, Code: variant.Code, AmountMinor: variant.AmountMinor,
                ApparelProduct: new(model, variant.Size, variant.Color));
            await SaveProductAsync(new CommerceTransaction(work.Db, work.Business, product, work.Now, work.Ct));
        }
        return variants[0].Id;
    }

    // Valida la ficha del producto y el código antes de guardar. Cuando el rubro lo admite, solicita un código automático si falta.
    private static async Task<Guid> SaveProductAsync(CommerceTransaction work)
    {
        var input = work.Command;
        var name = Text(input.Name, 100, "el nombre del producto", true);
        var code = input.EntityId is null && string.IsNullOrWhiteSpace(input.Code) && BusinessCodes.ProductPrefix(work.Business.Industry) is { } prefix
            ? await BusinessCodes.NextProductAsync(work, prefix)
            : Text(input.Code, 40, "el código del producto", true);
        var normalizedCode = Text(code.ToUpperInvariant(), 40, "el código del producto", true);
        var price = Money(input.TimberProduct?.ShortMinor ?? input.AmountMinor, "un precio");
        var products = work.Db.Set<Product>();
        if (products.Local.Any(x => x.BusinessId == work.Business.Id && x.NormalizedCode == normalizedCode && x.Id != input.EntityId) ||
            await products.AnyAsync(x => x.NormalizedCode == normalizedCode && x.Id != input.EntityId, work.Ct))
            throw new CommerceFailure("Ya existe un producto con ese código.", 409);

        Product product;
        if (input.EntityId is Guid existing)
        {
            product = await work.ProductAsync(existing);
            product.Name = name;
            product.Code = code;
            product.Active = input.Active ?? product.Active;
        }
        else
        {
            product = new Product { BusinessId = work.Business.Id, Id = input.OperationId, Name = name, Code = code,
                IsService = input.ServiceProduct == true };
            products.Add(product);
        }
        product.NormalizedCode = normalizedCode;
        TimberInventory.SetProfile(product, input.TimberProduct, work.Business.Industry == "timber", input.EntityId is null);
        await StoreProfiles.SaveAsync(work, product);
        await SpecialtyProfiles.SaveAsync(work, product);
        await MaterialProfiles.SaveAsync(work, product);
        if (input.ServiceProduct is bool service && service != product.IsService)
            throw new CommerceFailure("Crea otra ficha para cambiar entre producto y servicio.");
        if (product.IsService && (!(WorkOperations.Supports(work.Business.Industry) || work.Business.Industry == "timber") || product.RetailUnit is not null ||
            product.Species is not null || product.Model is not null || product.WarrantyMonths is not null || product.PrintingSize is not null))
            throw new CommerceFailure("El servicio requiere una ficha sin control de inventario en un rubro compatible.");
        product.PriceMinor = price;
        return product.Id;
    }

    // Crea o actualiza la ficha que luego se puede asociar a una venta o un fiado. Un cliente nuevo puede recibir su propio código.
    private static async Task<Guid> SaveCustomerAsync(CommerceTransaction work)
    {
        var input = work.Command;
        var name = Text(input.Name, 100, "el nombre del cliente", true);
        var phone = Text(input.Phone, 32, "el teléfono");
        if (input.EntityId is Guid id)
        {
            var record = await work.CustomerAsync(id);
            record.Name = name;
            record.Phone = phone;
            return record.Id;
        }

        var code = BusinessCodes.ProductPrefix(work.Business.Industry) is not null ? await BusinessCodes.NextAsync(work, "CLI") : null;
        var customer = new Customer { BusinessId = work.Business.Id, Id = input.OperationId, Name = name, Phone = phone, Code = code };
        work.Db.Add(customer);
        return customer.Id;
    }
}
