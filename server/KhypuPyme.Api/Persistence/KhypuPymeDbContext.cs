using KhypuPyme.Api.Accounts;
using KhypuPyme.Api.Businesses;
using KhypuPyme.Api.Billing;
using KhypuPyme.Api.Commerce;
using KhypuPyme.Api.Timber;
using KhypuPyme.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

// Es el punto donde EF Core reúne las entidades y la configuración con que se guardan en PostgreSQL. El contexto de una operación sigue los cambios hasta que se envían a la base.
namespace KhypuPyme.Api.Persistence;

public sealed class KhypuPymeDbContext(DbContextOptions<KhypuPymeDbContext> options)
    : IdentityUserContext<IdentityUser<Guid>, Guid>(options)
{
    // El esquema es el grupo de tablas de KhypuPyme dentro de la base. En pgAdmin se abre desde Schemas, khypupyme y Tables.
    public const string Schema = "khypupyme";
    // Un DbSet es el punto de entrada para consultar o agregar entidades de ese tipo. El nombre de su tabla se define más abajo en el mapeo.
    public DbSet<EmailChallenge> EmailChallenges => Set<EmailChallenge>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();
    public DbSet<AccountSelection> AccountSelections => Set<AccountSelection>();

    // Este método construye el mapa que traduce clases, propiedades y relaciones de C# en tablas, columnas y claves de PostgreSQL.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Conserva primero el modelo de ASP.NET Core Identity para cuentas. Las líneas siguientes añaden el esquema y las entidades propias de la aplicación.
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        // Añade el modelo que usa OpenIddict para las aplicaciones, autorizaciones y tokens del flujo de clientes instalados.
        modelBuilder.UseOpenIddict<Guid>();
        CommerceModel.Configure(modelBuilder);
        BillingModel.Configure(modelBuilder);
        SecurityModel.Configure(modelBuilder);
        // El índice único usa el correo normalizado de Identity para impedir cuentas con el mismo correo escrito con distintas mayúsculas.
        modelBuilder.Entity<IdentityUser<Guid>>().HasIndex(user => user.NormalizedEmail).IsUnique();

        // El correo y el propósito forman la clave. Verificar una cuenta y recuperar una contraseña tienen retos distintos.
        modelBuilder.Entity<EmailChallenge>(entry =>
        {
            entry.ToTable("email_challenges");
            entry.HasKey(x => new { x.Email, x.Purpose });
            entry.Property(x => x.Email).HasMaxLength(256);
            entry.Property(x => x.Purpose).HasMaxLength(16);
        });

        // La clave alternativa de propietario y negocio permite que algunas relaciones comprueben que ambos registros pertenecen a la misma cuenta.
        modelBuilder.Entity<Business>(entry =>
        {
            entry.ToTable("businesses");
            entry.HasKey(x => x.Id);
            entry.HasAlternateKey(x => new { x.OwnerId, x.Id });
            entry.HasOne<Business>().WithMany().HasPrincipalKey(x => new { x.OwnerId, x.Id })
                .HasForeignKey(x => new { x.OwnerId, x.PracticeOriginId }).OnDelete(DeleteBehavior.NoAction);
            entry.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(x => x.OwnerId);
            entry.Property(x => x.Name).HasMaxLength(100);
            entry.Property(x => x.Industry).HasMaxLength(32);
            entry.Property(x => x.CreationDigest).HasMaxLength(64);
            entry.HasIndex(x => new { x.OwnerId, x.Name, x.Id });
        });

        // Vincula la práctica con su negocio de origen y con un espacio separado. Así se pueden consultar los ejercicios sin mezclar sus registros con el negocio habitual.
        modelBuilder.Entity<TimberPractice>(entry =>
        {
            entry.ToTable("timber_practices");
            entry.HasKey(x => new { x.OwnerId, x.OriginId });
            entry.HasIndex(x => x.SandboxId).IsUnique();
            entry.HasOne<Business>().WithMany().HasPrincipalKey(x => new { x.OwnerId, x.Id })
                .HasForeignKey(x => new { x.OwnerId, x.OriginId });
        });

        // Separa los datos de configuración del negocio para guardar sus preferencias y reglas operativas.
        modelBuilder.Entity<BusinessSettings>(entry =>
        {
            entry.ToTable("business_settings", table =>
            {
                table.HasCheckConstraint("business_settings_payment", "\"DefaultPayment\" IN ('cash', 'qr', 'transfer')");
                table.HasCheckConstraint("business_settings_discount", "\"MaxDiscountPercent\" BETWEEN 0 AND 100");
                table.HasCheckConstraint("business_settings_days", "\"QuoteDays\" BETWEEN 1 AND 90 AND \"LotAlertDays\" BETWEEN 1 AND 90");
                table.HasCheckConstraint("business_settings_revision", "\"PolicyRevision\" >= 0");
            });
            entry.HasKey(x => x.BusinessId);
            entry.HasOne<Business>().WithOne().HasPrincipalKey<Business>(x => new { x.OwnerId, x.Id })
                .HasForeignKey<BusinessSettings>(x => new { x.OwnerId, x.BusinessId });
            entry.Property(x => x.City).HasMaxLength(80);
            entry.Property(x => x.Phone).HasMaxLength(32);
            entry.Property(x => x.TimberUnits).HasMaxLength(10);
            entry.Property(x => x.DefaultPayment).HasMaxLength(10).HasDefaultValue(BusinessPolicy.DefaultPayment);
            entry.Property(x => x.MaxDiscountPercent).HasDefaultValue(BusinessPolicy.MaxDiscountPercent).HasSentinel(-1);
            entry.Property(x => x.QuoteDays).HasDefaultValue(BusinessPolicy.QuoteDays);
            entry.Property(x => x.AllowNewCredit).HasDefaultValue(true);
            entry.Property(x => x.LotAlertDays).HasDefaultValue(BusinessPolicy.LotAlertDays);
            entry.Property(x => x.PolicyRevision).HasDefaultValue(0);
        });

        // Relaciona la selección recordada de una cuenta con el negocio. Las claves impiden enlazar el recuerdo con un negocio ajeno.
        modelBuilder.Entity<AccountSelection>(entry =>
        {
            entry.ToTable("account_selections");
            entry.HasKey(x => x.OwnerId);
            entry.HasOne<Business>().WithMany().HasPrincipalKey(x => new { x.OwnerId, x.Id })
                .HasForeignKey(x => new { x.OwnerId, x.BusinessId });
        });
    }
}
