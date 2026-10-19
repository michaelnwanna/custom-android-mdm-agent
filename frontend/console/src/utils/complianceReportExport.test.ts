import { describe, expect, it } from 'vitest'
import {
  buildComplianceCsv,
  buildCompliancePrintHtml,
  formatComplianceDate,
  type ComplianceReportRow
} from './complianceReportExport'

const sampleRow: ComplianceReportRow = {
  deviceId: 'device-001',
  deviceName: 'Device, "Alpha"',
  policyName: 'Security',
  status: 'compliant',
  compliance: 97,
  lastChecked: '2026-10-19T12:00:00.000Z'
}

describe('buildComplianceCsv', () => {
  it('includes all report rows and a UTF-8 BOM', () => {
    const csv = buildComplianceCsv([sampleRow, { ...sampleRow, deviceId: 'device-002' }], value => value)
    expect(csv.charCodeAt(0)).toBe(0xfeff)
    expect(csv).toContain('"device-001"')
    expect(csv).toContain('"device-002"')
  })

  it('quotes values and escapes embedded double quotes', () => {
    const csv = buildComplianceCsv([sampleRow], value => value)
    expect(csv).toContain('"Device, ""Alpha"""')
  })

  it.each(['=1+1', '+SUM(A1:A2)', '-10', '@value'])(
    'neutralizes spreadsheet formulas beginning with %s',
    value => {
      const csv = buildComplianceCsv([{ ...sampleRow, deviceName: value }], item => item)
      expect(csv).toContain(`"'${value}"`)
    }
  )

  it('formats the last-checked value using the supplied formatter', () => {
    const csv = buildComplianceCsv([sampleRow], () => 'formatted date')
    expect(csv).toContain('"formatted date"')
  })
})

describe('buildCompliancePrintHtml', () => {
  const summary = {
    complianceRate: 97,
    compliantDevices: 1,
    nonCompliantDevices: 0,
    totalDevices: 1,
    startDate: '2026-10-01',
    endDate: '2026-10-19'
  }

  it('renders report summary and all rows', () => {
    const html = buildCompliancePrintHtml([sampleRow], summary, () => 'formatted date')
    expect(html).toContain('97%')
    expect(html).toContain('device-001')
    expect(html).toContain('formatted date')
  })

  it('escapes data before inserting it into HTML', () => {
    const html = buildCompliancePrintHtml(
      [{ ...sampleRow, deviceName: '<script>alert("x")</script>' }],
      summary,
      () => 'date'
    )
    expect(html).toContain('&lt;script&gt;')
    expect(html).not.toContain('<script>alert')
  })

  it('prints a helpful empty state when there are no matching rows', () => {
    const html = buildCompliancePrintHtml([], { ...summary, totalDevices: 0 }, () => 'date')
    expect(html).toContain('No devices match the selected filters.')
  })
})

describe('formatComplianceDate', () => {
  it('returns the original value for invalid dates', () => {
    expect(formatComplianceDate('not-a-date')).toBe('not-a-date')
  })

  it('formats valid dates', () => {
    expect(formatComplianceDate('2026-10-19T12:00:00.000Z')).not.toBe(
      '2026-10-19T12:00:00.000Z'
    )
  })
})
