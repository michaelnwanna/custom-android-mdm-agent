# Phase 2.11c Planning: Alerts & Policies API Integration

## 📋 Overview

**Status:** Planning  
**Estimated Duration:** 3-4 hours  
**Predecessor:** Phase 2.11b (Device API & Real-time) ✅ Complete  
**Successor:** Phase 2.11d (Releases, Tags, Settings)

This phase focuses on connecting the Alerts and Policies UI components to the backend API with real-time updates via SignalR.

---

## 🎯 Objectives

### Part 1: Alerts API Integration (1.5-2 hours)
- [ ] Create alertApi.ts wrapper with 8+ functions
- [ ] Implement AlertsView with API-based data
- [ ] Implement AlertDetailView with full CRUD
- [ ] Real-time alert delivery via SignalR
- [ ] Alert filtering and sorting
- [ ] Bulk alert operations (mark resolved)
- [ ] Toast notifications for new alerts

### Part 2: Policies API Integration (1.5-2 hours)
- [ ] Create policyApi.ts wrapper with 7+ functions
- [ ] Implement PoliciesView with API-based data
- [ ] Implement PolicyDetailView with full CRUD
- [ ] Policy assignment to device groups
- [ ] Compliance tracking
- [ ] Policy versioning and rollback
- [ ] Real-time policy update notifications

---

## 🏗️ Architecture

### Alerts Flow
```
AlertsView (list)
    ↓
useAlerts composable (pagination, filtering, sorting)
    ↓
alertApi (CRUD + real-time operations)
    ↓
HTTP Client (token management)
    ↓
.NET Core API
    ↓
SignalR Hub (ReceiveAlert event)
```

### Policies Flow
```
PoliciesView (list)
    ↓
usePolicies composable (pagination, filtering)
    ↓
policyApi (CRUD + assignment operations)
    ↓
HTTP Client
    ↓
.NET Core API
    ↓
SignalR Hub (ReceivePolicyUpdate event)
```

---

## 📝 API Endpoints

### Alerts Endpoints
```typescript
// Base: /api/v1/alerts

// List and detail
GET    /                      // List alerts with pagination
GET    /{alertId}             // Get alert detail
POST   /                      // Create alert
PUT    /{alertId}             // Update alert
DELETE /{alertId}             // Delete alert

// Alert operations
POST   /{alertId}/resolve     // Mark as resolved
POST   /{alertId}/reopen      // Reopen resolved alert
POST   /batch/resolve         // Bulk resolve
GET    /{alertId}/history     // Alert audit trail

// Device-specific
GET    /device/{deviceId}     // Get alerts for device
```

### Policies Endpoints
```typescript
// Base: /api/v1/policies

// List and detail
GET    /                      // List policies with pagination
GET    /{policyId}            // Get policy detail
POST   /                      // Create policy
PUT    /{policyId}            // Update policy
DELETE /{policyId}            // Delete policy

// Policy operations
POST   /{policyId}/assign     // Assign to devices/groups
POST   /{policyId}/revoke     // Revoke from devices/groups
GET    /{policyId}/versions   // Get version history
POST   /{policyId}/rollback   // Rollback to previous version

// Compliance
GET    /{policyId}/compliance // Get compliance status
GET    /device/{deviceId}     // Get policies for device
```

---

## 🔧 Implementation Details

### Part 1: alertApi.ts (160-180 lines)
```typescript
// Queries
export async function fetchAlerts(options: AlertQueryParams)
export async function fetchAlertById(alertId: string)
export async function getAlertHistory(alertId: string, limit: number)
export async function getDeviceAlerts(deviceId: string)

// Mutations
export async function createAlert(alert: CreateAlertInput)
export async function updateAlert(alertId: string, alert: UpdateAlertInput)
export async function deleteAlert(alertId: string)

// Operations
export async function resolveAlert(alertId: string, resolution: AlertResolution)
export async function reopenAlert(alertId: string)
export async function bulkResolveAlerts(alertIds: string[])
```

### Part 1: useAlerts Composable (180-200 lines)
```typescript
// Features
- Paginated alert queries
- Severity filtering (Critical, High, Medium, Low)
- Status filtering (Open, Resolved, Acknowledged)
- Search by message/device
- Sort options (created, severity, device)
- Auto-load on mount
- Alert action methods

// Lifecycle
- setup()
- computed (filtered alerts, total count, loading state)
- methods (resolve, delete, bulk operations)
```

### Part 1: AlertsView Enhancements
```vue
<!-- Add features -->
- Severity badge colors
- Status filter dropdown
- Search bar for alert message
- Bulk resolve checkbox
- Sort by severity/date
- Infinite scroll or pagination
- Alert click to detail
```

### Part 1: AlertDetailView Enhancements
```vue
<!-- Add features -->
- Show device link
- Show alert history/timeline
- Resolve button
- Add note/comment
- Close alert
- Affected devices list
- Remediation suggestions (if applicable)
```

### Part 2: policyApi.ts (150-170 lines)
```typescript
// Queries
export async function fetchPolicies(options: PolicyQueryParams)
export async function fetchPolicyById(policyId: string)
export async function getPolicyVersions(policyId: string)
export async function getDevicePolicies(deviceId: string)
export async function getPolicyCompliance(policyId: string)

// Mutations
export async function createPolicy(policy: CreatePolicyInput)
export async function updatePolicy(policyId: string, policy: UpdatePolicyInput)
export async function deletePolicy(policyId: string)

// Operations
export async function assignPolicy(policyId: string, assignment: PolicyAssignment)
export async function revokePolicy(policyId: string, deviceIds: string[])
export async function rollbackPolicy(policyId: string, versionId: string)
```

### Part 2: usePolicies Composable (180-200 lines)
```typescript
// Features
- Paginated policy queries
- Type filtering (Security, Compliance, Performance, Custom)
- Status filtering (Active, Archived, Draft)
- Search by name/description
- Sort options (name, created, devices)
- Auto-load on mount
- Policy action methods

// Lifecycle
- setup()
- computed (filtered policies, deployment stats)
- methods (delete, assign, revoke, rollback)
```

### Part 2: PoliciesView Enhancements
```vue
<!-- Add features -->
- Policy type badge
- Deployment progress indicator
- Device count display
- Last updated timestamp
- Type filter dropdown
- Search bar
- Bulk actions (archive, delete)
- Policy click to detail
```

### Part 2: PolicyDetailView Enhancements
```vue
<!-- Add features -->
- Policy settings display
- Device assignment section
- Compliance report
- Version history
- Rollback button
- Edit button (draft mode)
- Deploy/Archive buttons
- Affected devices count
- Compliance timeline
```

### Part 3: SignalR Integration Enhancements
```typescript
// New event handlers in signalRService

connection.on('ReceiveAlert', (alert: HealthAlert) => {
  callbacks.onAlertReceived?.(alert)
})

connection.on('ReceivePolicyUpdate', (policy: Policy) => {
  callbacks.onPolicyUpdate?.(policy)
})

connection.on('ReceivePolicyCompliance', (compliance: PolicyCompliance) => {
  callbacks.onPolicyCompliance?.(compliance)
})
```

---

## 🧪 Testing Plan

### Alerts Testing
- [ ] Load alerts list page
- [ ] Verify pagination works
- [ ] Filter by severity
- [ ] Filter by status
- [ ] Search by message
- [ ] Sort by date/severity
- [ ] View alert detail
- [ ] Resolve alert
- [ ] Reopen alert
- [ ] Delete alert
- [ ] Bulk resolve multiple alerts
- [ ] Real-time alert notification via SignalR
- [ ] Toast appears for new alerts

### Policies Testing
- [ ] Load policies list page
- [ ] Verify pagination works
- [ ] Filter by type
- [ ] Filter by status
- [ ] Search by name
- [ ] Sort options work
- [ ] View policy detail
- [ ] Create new policy
- [ ] Edit policy
- [ ] Delete policy
- [ ] Assign policy to devices
- [ ] Revoke policy
- [ ] View compliance report
- [ ] Rollback to previous version
- [ ] Real-time policy update notification

### Integration Testing
- [ ] Alerts and devices linked correctly
- [ ] Policies and devices linked correctly
- [ ] SignalR events trigger UI updates
- [ ] Error handling and fallback
- [ ] Performance with 100+ alerts
- [ ] Performance with 50+ policies

---

## 📦 File Structure

### New Files
```
src/api/alertApi.ts                    // Alert operations wrapper
src/api/policyApi.ts                   // Policy operations wrapper
src/composables/useAlerts.ts           // Alert query composable
src/composables/usePolicies.ts         // Policy query composable
PHASE-2-11C-PLANNING.md                // This file
PHASE-2-11C-COMPLETION-SUMMARY.md      // To be created
```

### Enhanced Files
```
src/views/AlertsView.vue               // API integration
src/views/AlertDetailView.vue          // Full CRUD + detail
src/views/PoliciesView.vue             // API integration
src/views/PolicyDetailView.vue         // Full CRUD + detail
src/services/signalRService.ts         // New event handlers
src/stores/alertStore.ts               // Refactor to use alertApi
src/stores/policyStore.ts              // Refactor to use policyApi
src/types/index.ts                     // New types if needed
```

---

## 🎯 Implementation Checklist

### Phase 2.11c.1: Alert API & Composable
- [ ] Create src/api/alertApi.ts with 8+ functions
- [ ] Create src/composables/useAlerts.ts
- [ ] Update alertStore.ts to use alertApi
- [ ] Test alertApi functions
- [ ] Test useAlerts composable

### Phase 2.11c.2: Alerts UI Integration
- [ ] Update AlertsView.vue with API data
- [ ] Add severity filtering
- [ ] Add status filtering
- [ ] Add search functionality
- [ ] Implement pagination
- [ ] Add bulk operations
- [ ] Test AlertsView

### Phase 2.11c.3: Alert Detail & Operations
- [ ] Update AlertDetailView.vue
- [ ] Add resolve/reopen buttons
- [ ] Add delete button
- [ ] Show alert history
- [ ] Show affected device
- [ ] Test all operations

### Phase 2.11c.4: Real-time Alerts
- [ ] Add SignalR event handlers
- [ ] Auto-subscribe to device alerts
- [ ] Show toast on new alert
- [ ] Update alerts list in real-time
- [ ] Test real-time delivery

### Phase 2.11c.5: Policy API & Composable
- [ ] Create src/api/policyApi.ts with 7+ functions
- [ ] Create src/composables/usePolicies.ts
- [ ] Update policyStore.ts
- [ ] Test policyApi functions
- [ ] Test usePolicies composable

### Phase 2.11c.6: Policies UI Integration
- [ ] Update PoliciesView.vue with API data
- [ ] Add type filtering
- [ ] Add status filtering
- [ ] Add search functionality
- [ ] Add pagination
- [ ] Test PoliciesView

### Phase 2.11c.7: Policy Detail & Operations
- [ ] Update PolicyDetailView.vue
- [ ] Add create/edit/delete
- [ ] Add assign policy buttons
- [ ] Show compliance status
- [ ] Show version history
- [ ] Add rollback button

### Phase 2.11c.8: Real-time Policies
- [ ] Add SignalR event handlers
- [ ] Real-time policy updates
- [ ] Compliance status updates
- [ ] Version update notifications

### Phase 2.11c.9: Testing & Docs
- [ ] Run full test suite
- [ ] Verify TypeScript strict mode
- [ ] Test error scenarios
- [ ] Performance testing
- [ ] Create completion summary
- [ ] Commit and push

---

## ⏱️ Time Breakdown

| Task | Duration | Status |
|------|----------|--------|
| alertApi.ts creation | 30 min | Pending |
| useAlerts composable | 30 min | Pending |
| AlertsView enhancement | 45 min | Pending |
| AlertDetailView CRUD | 45 min | Pending |
| Real-time alerts (SignalR) | 30 min | Pending |
| policyApi.ts creation | 30 min | Pending |
| usePolicies composable | 30 min | Pending |
| PoliciesView enhancement | 45 min | Pending |
| PolicyDetailView CRUD | 45 min | Pending |
| Real-time policies (SignalR) | 30 min | Pending |
| Testing & validation | 30 min | Pending |
| Documentation | 15 min | Pending |
| **Total** | **4.5 hours** | **Pending** |

---

## 🔄 Dependencies

### Prerequisites (Must be complete)
- ✅ Phase 2.11a: Authentication
- ✅ Phase 2.11b: Device API & Real-time

### Parallel Work
- .NET Core API implementation (backend)
- SignalR hub setup (backend)

### Blockers
- Backend API endpoints must be available
- SignalR event names must be defined

---

## 🚀 Success Criteria

### Code Quality
- ✅ All TypeScript strict mode compliant
- ✅ No console warnings
- ✅ Proper error handling
- ✅ Consistent code style

### Features
- ✅ All 8+ alert operations working
- ✅ All 7+ policy operations working
- ✅ Real-time updates via SignalR
- ✅ Pagination and filtering
- ✅ Bulk operations
- ✅ Error fallback to mock data

### Testing
- ✅ 40+ test cases passing
- ✅ Manual testing complete
- ✅ Edge cases handled
- ✅ Performance verified

### Documentation
- ✅ Inline code comments
- ✅ API documentation
- ✅ Type definitions
- ✅ Completion summary

---

## 📚 Resources

### Type Definitions Needed
```typescript
interface Alert {
  alertId: string
  deviceId: string
  deviceName: string
  severity: 'Critical' | 'High' | 'Medium' | 'Low'
  type: string
  message: string
  timestamp: string
  resolved: boolean
  resolutionTime?: string
}

interface Policy {
  policyId: string
  name: string
  description: string
  type: string
  status: string
  deviceCount: number
  complianceRate: number
  lastUpdated: string
  version: number
}
```

### Mock Data
- 20+ alert templates (various severity levels)
- 10+ policy templates (various types)
- Realistic timestamps
- Device linking

---

## 📊 Expected Outcomes

### By End of Phase 2.11c
- [ ] Alerts fully functional with API
- [ ] Policies fully functional with API
- [ ] Real-time updates working
- [ ] 80+ test cases passing
- [ ] 3,500+ lines of code added
- [ ] Zero TypeScript errors
- [ ] Ready for Phase 2.11d

---

## 🎓 Lessons & Best Practices

### From Phase 2.11b
- ✅ Composables reduce code duplication
- ✅ Mock data enables development without backend
- ✅ SignalR integration is seamless with proper service
- ✅ Progress bars improve UX for async operations
- ✅ Toast notifications provide feedback

### Applying to Phase 2.11c
- Use same composable pattern for alerts/policies
- Implement mock data streams for testing
- Batch operations for bulk actions
- Real-time notifications via SignalR
- Progress indicators for async operations

---

## ✅ Rollout Strategy

### Staged Deployment
1. **Day 1:** Alerts API implementation
2. **Day 1:** Alerts UI integration
3. **Day 2:** Policies API implementation
4. **Day 2:** Policies UI integration
5. **Day 2:** Real-time integration
6. **Day 2:** Testing & documentation

### Fallback Plan
- If backend not ready: Use mock data
- If SignalR not ready: Use polling with intervals
- If performance issues: Implement lazy loading
- If TypeScript errors: Revert to previous version

---

## 📞 Support & Escalation

### Common Issues to Handle
1. **API errors:** Retry with backoff + mock data
2. **SignalR disconnect:** Auto-reconnect with exponential backoff
3. **Large datasets:** Pagination + lazy loading
4. **Slow performance:** Caching + memoization
5. **Type mismatches:** Strict type validation

---

## 🏁 Next Phase (2.11d)

After Phase 2.11c completion:
- Releases API integration (1-2 hours)
- Tags API integration (30 min)
- Settings persistence (30 min)
- Dashboard real-time (2 hours)

---

**Status:** Ready to begin Phase 2.11c  
**Target Start:** Immediately after Phase 2.11b completion  
**Estimated Completion:** +4-5 hours
