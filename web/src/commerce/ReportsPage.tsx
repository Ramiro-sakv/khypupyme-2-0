import { useState } from "react";
import { ReportChart } from "./ReportChart";
import { Alert, Button, Card, TextField } from "../ui/Primitives";
import { useCommerceData } from "./CommerceContext";
import { CommerceHeading, LoadFeedback } from "./CommerceUi";
import { bob, dateLabel, reportResult } from "./models";
import type { Report } from "./models";

export function ReportsPage({ business, practice }: { business: string; practice: boolean }) {
  const [saving, setSaving] = useState(false);
  const [pdfError, setPdfError] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [period, setPeriod] = useState("");
  const [rangeError, setRangeError] = useState("");
  const data = useCommerceData<Report>("/report?" + period);
  const report = data.data;
  return <div className="commerce-stack">
    <CommerceHeading title="Informes">{report && <Button variant="secondary" icon="document" disabled={saving} onClick={async () => {
      setSaving(true);
      setPdfError("");
      try { await (await import("./pdf")).downloadReport(report, business, practice); }
      catch { setPdfError("No se pudo guardar el PDF del informe. Vuelve a intentar."); }
      finally { setSaving(false); }
    }}>{saving ? "Preparando PDF…" : "Guardar informe PDF"}</Button>}</CommerceHeading>
    {pdfError && <Alert tone="error">{pdfError}</Alert>}
    <Card>
      <form className="inline-actions report-range" onSubmit={(event) => {
        event.preventDefault();
        if (from && to) {
          const daysBetween = (Date.parse(to) - Date.parse(from)) / 86_400_000;
          if (daysBetween < 0) {
            setRangeError("La fecha inicial debe ser igual o anterior a la fecha final.");
            return;
          }
          if (daysBetween >= 366) {
            setRangeError("Elige un periodo de hasta 366 días, incluyendo ambas fechas.");
            return;
          }
        }
        setRangeError("");
        const parameters = new URLSearchParams();
        if (from) parameters.set("from", from);
        if (to) parameters.set("to", to);
        const next = parameters.toString();
        if (next === period) data.reload();
        else setPeriod(next);
      }}>
        <TextField label="Desde" type="date" value={from} aria-invalid={!!rangeError}
          onChange={(event) => { setFrom(event.target.value); setRangeError(""); }} />
        <TextField label="Hasta" type="date" value={to} aria-invalid={!!rangeError}
          onChange={(event) => { setTo(event.target.value); setRangeError(""); }} />
        <Button type="submit">Consultar periodo</Button>
      </form>
      {rangeError && <Alert tone="error">{rangeError}</Alert>}
      {report && <p>{dateLabel(report.from)} al {dateLabel(report.to)} · Hora de Bolivia</p>}
    </Card>
    {!report ? <LoadFeedback {...data} /> : <>
      <div className="report-totals">
        {([
          ["Ventas netas", report.totals.salesMinor], ["Costo de lo vendido", report.totals.costMinor],
          ["Gastos operativos", report.totals.expensesMinor], ["Pérdidas de inventario", report.totals.lossesMinor],
          ["Resultado estimado", reportResult(report.totals)], ["Compras de mercadería", report.totals.purchasesMinor],
          ["Inventario a costo actual", report.stockValueMinor], ["Fiados pendientes actuales", report.receivablesMinor],
        ] as const).map(([title, amount]) => <Card key={title}><h2>{title}</h2><p className="trade-total">{bob(amount)}</p></Card>)}
      </div>
      <p>El resultado descuenta costo de lo vendido, gastos y pérdidas. Las compras se registran por separado.</p>
      <ReportChart days={report.days} />
    </>}
  </div>;
}
