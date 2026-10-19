export interface ComplianceReportRow {
  deviceId: string
  deviceName: string
  policyName: string
  status: string
  compliance: number
  lastChecked: string
}

export interface ComplianceReportSummary {
  complianceRate: number
  compliantDevices: number
  nonCompliantDevices: number
  totalDevices: number
  startDate: string
  endDate: string
}

const escapeHtml = (value: string): string =>
  value.replace(/[&<>"']/g, character => {
    const entities: Record<string, string> = {
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    }
    return entities[character] || character
  })

export const formatComplianceDate = (dateString: string): string => {
  const date = new Date(dateString)
  if (Number.isNaN(date.getTime())) return dateString
  return `${date.toLocaleDateString()} ${date.toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit'
  })}`
}

export const buildComplianceCsv = (
  rows: ComplianceReportRow[],
  formatDate: (dateString: string) => string
): string => {
  const headers = ['Device ID', 'Device Name', 'Policy', 'Status', 'Compliance', 'Last Checked']
  const csvCell = (value: string | number): string => {
    const text = String(value)
    const spreadsheetSafe = /^[\s]*[=+\-@]/.test(text) ? `'${text}` : text
    return `"${spreadsheetSafe.replace(/"/g, '""')}"`
  }
  const records = rows.map(row => [
    row.deviceId,
    row.deviceName,
    row.policyName,
    row.status,
    `${row.compliance}%`,
    formatDate(row.lastChecked)
  ])

  return `\uFEFF${[headers, ...records].map(record => record.map(csvCell).join(',')).join('\r\n')}`
}

export const buildCompliancePrintHtml = (
  rows: ComplianceReportRow[],
  summary: ComplianceReportSummary,
  formatDate: (dateString: string) => string
): string => {
  const tableRows = rows.map(row => `
    <tr>
      <td>${escapeHtml(row.deviceId)}</td>
      <td>${escapeHtml(row.deviceName)}</td>
      <td>${escapeHtml(row.policyName)}</td>
      <td>${escapeHtml(row.status)}</td>
      <td>${row.compliance}%</td>
      <td>${escapeHtml(formatDate(row.lastChecked))}</td>
    </tr>`).join('')

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Device Compliance Report</title>
  <style>
    body { color: #111827; font: 14px Arial, sans-serif; margin: 32px; }
    h1 { margin: 0 0 8px; }
    .meta { color: #4b5563; margin-bottom: 24px; }
    .summary { display: flex; gap: 24px; margin-bottom: 24px; }
    .summary div { border: 1px solid #d1d5db; border-radius: 6px; flex: 1; padding: 12px; }
    .summary strong { display: block; font-size: 20px; margin-top: 6px; }
    table { border-collapse: collapse; width: 100%; }
    th, td { border: 1px solid #d1d5db; padding: 8px; text-align: left; }
    th { background: #f3f4f6; }
    tr { break-inside: avoid; }
    @media print { body { margin: 16mm; } }
  </style>
</head>
<body>
  <h1>Device Compliance Report</h1>
  <div class="meta">Report generated ${escapeHtml(formatComplianceDate(new Date().toISOString()))} · Period: ${escapeHtml(summary.startDate)} to ${escapeHtml(summary.endDate)}</div>
  <section class="summary">
    <div>Devices<strong>${summary.totalDevices}</strong></div>
    <div>Compliant<strong>${summary.compliantDevices}</strong></div>
    <div>Non-compliant<strong>${summary.nonCompliantDevices}</strong></div>
    <div>Compliance rate<strong>${summary.complianceRate}%</strong></div>
  </section>
  <table>
    <thead><tr><th>Device ID</th><th>Device</th><th>Policy</th><th>Status</th><th>Compliance</th><th>Last Checked</th></tr></thead>
    <tbody>${tableRows || '<tr><td colspan="6">No devices match the selected filters.</td></tr>'}</tbody>
  </table>
</body>
</html>`
}
