import { HubConnectionBuilder, HubConnectionState, HubConnection } from '@microsoft/signalr';
import type { Device, HealthAlert, FleetHealthMetrics, DeviceTelemetry, Policy } from '@types';

export type ConnectionState = 'connected' | 'connecting' | 'disconnected' | 'failed';

export interface SignalRServiceCallbacks {
  onFleetMetricsUpdate?: (metrics: FleetHealthMetrics) => void;
  onAlertReceived?: (alert: HealthAlert) => void;
  onDeviceStatusChanged?: (device: Device) => void;
  onDeviceTelemetryUpdate?: (deviceId: string, telemetry: DeviceTelemetry) => void;
  onConnectionStateChanged?: (state: ConnectionState) => void;
  onDeviceOnlineStatusChanged?: (deviceId: string, isOnline: boolean) => void;
  onAlertUpdated?: (alert: HealthAlert) => void;
  onAlertResolved?: (alertId: string) => void;
  onPolicyUpdated?: (policy: Policy) => void;
  onPolicyDeployed?: (policyId: string, deviceIds: string[]) => void;
  onPolicyComplianceChanged?: (policyId: string, compliancePercentage: number) => void;
}

class SignalRService {
  private connection: HubConnection | null = null;
  private connectionState: ConnectionState = 'disconnected';
  private reconnectAttempts = 0;
  private maxReconnectAttempts = 5;
  private reconnectDelay = 1000; // 1 second
  private useMockData = true; // Use mock hub when backend not available
  private mockDataInterval: ReturnType<typeof setInterval> | null = null;
  private mockPolicyInterval: ReturnType<typeof setInterval> | null = null;
  private mockTelemetryIntervals: Map<string, ReturnType<typeof setInterval>> = new Map();
  private callbacks: SignalRServiceCallbacks = {};
  private subscribedDeviceIds: Set<string> = new Set();

  async connect(callbacks: SignalRServiceCallbacks = {}): Promise<void> {
    this.callbacks = callbacks;
    this.setConnectionState('connecting');

    if (this.useMockData) {
      console.log('🔌 SignalR: Using mock hub simulation (backend not available)');
      this.startMockHub();
      return;
    }

    try {
      this.connection = new HubConnectionBuilder()
        .withUrl(import.meta.env.VITE_API_URL?.replace('http', 'ws') + '/deviceHub', {
          skipNegotiation: false,
          transport: 1, // WebSockets
        })
        .withAutomaticReconnect([0, 0, 1000, 3000, 5000, 10000])
        .withServerTimeout(30000)
        .build();

      // Register event handlers
      this.connection.on('ReceiveFleetMetricsUpdate', (metrics: FleetHealthMetrics) => {
        console.log('📊 Received fleet metrics update:', metrics);
        this.callbacks.onFleetMetricsUpdate?.(metrics);
      });

      this.connection.on('ReceiveAlert', (alert: HealthAlert) => {
        console.log('🚨 Received alert:', alert);
        this.callbacks.onAlertReceived?.(alert);
      });

      this.connection.on('ReceiveDeviceStatusChange', (device: Device) => {
        console.log('📱 Device status changed:', device);
        this.callbacks.onDeviceStatusChanged?.(device);
      });

      // New device-specific event handlers
      this.connection.on('ReceiveDeviceTelemetry', (deviceId: string, telemetry: DeviceTelemetry) => {
        console.log(`📊 Device ${deviceId} telemetry update:`, telemetry);
        this.callbacks.onDeviceTelemetryUpdate?.(deviceId, telemetry);
      });

      this.connection.on('ReceiveDeviceOnlineStatusChange', (deviceId: string, isOnline: boolean) => {
        console.log(`🟢 Device ${deviceId} online status: ${isOnline ? 'ONLINE' : 'OFFLINE'}`);
        this.callbacks.onDeviceOnlineStatusChanged?.(deviceId, isOnline);
      });

      // Real-time alert event handlers
      this.connection.on('ReceiveAlertUpdated', (alert: HealthAlert) => {
        console.log('📢 Alert updated:', alert);
        this.callbacks.onAlertUpdated?.(alert);
      });

      this.connection.on('ReceiveAlertResolved', (alertId: string) => {
        console.log(`✅ Alert ${alertId} resolved`);
        this.callbacks.onAlertResolved?.(alertId);
      });

      // Real-time policy event handlers
      this.connection.on('ReceivePolicyUpdated', (policy: Policy) => {
        console.log('📋 Policy updated:', policy);
        this.callbacks.onPolicyUpdated?.(policy);
      });

      this.connection.on('ReceivePolicyDeployed', (policyId: string, deviceIds: string[]) => {
        console.log(`🚀 Policy ${policyId} deployed to ${deviceIds.length} devices`);
        this.callbacks.onPolicyDeployed?.(policyId, deviceIds);
      });

      this.connection.on('ReceivePolicyComplianceChanged', (policyId: string, compliancePercentage: number) => {
        console.log(`📊 Policy ${policyId} compliance: ${compliancePercentage}%`);
        this.callbacks.onPolicyComplianceChanged?.(policyId, compliancePercentage);
      });

      // Connection lifecycle events
      this.connection.onreconnecting(() => {
        console.log('🔄 SignalR: Attempting to reconnect...');
        this.setConnectionState('connecting');
      });

      this.connection.onreconnected(() => {
        console.log('✅ SignalR: Reconnected successfully');
        this.reconnectAttempts = 0;
        this.setConnectionState('connected');
        // Re-subscribe to devices after reconnection
        this.resubscribeDevices();
      });

      this.connection.onclose(() => {
        console.log('❌ SignalR: Connection closed');
        this.setConnectionState('disconnected');
      });

      await this.connection.start();
      this.reconnectAttempts = 0;
      this.setConnectionState('connected');
      console.log('✅ SignalR: Connected to hub');
    } catch (error) {
      console.error('❌ SignalR: Connection failed:', error);
      this.setConnectionState('failed');
      this.reconnectWithBackoff();
    }
  }

  private startMockHub(): void {
    this.setConnectionState('connected');
    console.log('✅ SignalR: Mock hub initialized');

    // Simulate periodic fleet metrics updates
    this.mockDataInterval = setInterval(() => {
      const mockMetrics: FleetHealthMetrics = {
        totalDevices: 150,
        onlineDevices: 142,
        offlineDevices: 8,
        activeAlerts: Math.floor(Math.random() * 15) + 5,
        criticalAlerts: Math.floor(Math.random() * 5),
        devicesByStatus: {
          healthy: 120,
          warning: 15,
          critical: 7,
          unknown: 8,
        },
        batteryDistribution: {
          charging: 45,
          discharging: 35,
          critical: 15,
          unknown: 5,
        },
        storageDistribution: [
          { deviceId: 'dev-001', name: 'Device 1', storageUsedPercent: Math.floor(Math.random() * 30) + 60 },
          { deviceId: 'dev-002', name: 'Device 2', storageUsedPercent: Math.floor(Math.random() * 30) + 70 },
          { deviceId: 'dev-003', name: 'Device 3', storageUsedPercent: Math.floor(Math.random() * 30) + 50 },
          { deviceId: 'dev-004', name: 'Device 4', storageUsedPercent: Math.floor(Math.random() * 30) + 80 },
          { deviceId: 'dev-005', name: 'Device 5', storageUsedPercent: Math.floor(Math.random() * 30) + 75 },
        ],
        healthScore: Math.floor(Math.random() * 30) + 70,
        lastUpdated: new Date().toISOString(),
      };

      this.callbacks.onFleetMetricsUpdate?.(mockMetrics);
    }, 15000); // Update every 15 seconds

    // Simulate periodic alert arrivals and updates
    const alertInterval = setInterval(() => {
      if (Math.random() > 0.7) { // 30% chance
        const mockAlert: HealthAlert = {
          alertId: `alert-${Date.now()}`,
          id: `alert-${Date.now()}`,
          deviceId: `dev-${Math.floor(Math.random() * 10).toString().padStart(3, '0')}`,
          deviceName: `Device ${Math.floor(Math.random() * 50)}`,
          severity: ['Critical', 'High', 'Medium'][Math.floor(Math.random() * 3)] as any,
          type: ['battery', 'storage', 'connectivity', 'compliance'][Math.floor(Math.random() * 4)] as any,
          message: 'Mock alert from SignalR',
          timestamp: new Date().toISOString(),
          createdAt: new Date().toISOString(),
          resolved: false,
        };

        this.callbacks.onAlertReceived?.(mockAlert);
      }

      // Simulate alert resolution
      if (Math.random() > 0.85) { // 15% chance
        const mockAlertId = `alert-${Date.now() - Math.random() * 100000}`;
        this.callbacks.onAlertResolved?.(mockAlertId);
      }

      // Simulate alert updates
      if (Math.random() > 0.9) { // 10% chance
        const updatedAlert: HealthAlert = {
          alertId: `alert-${Date.now() - Math.random() * 50000}`,
          id: `alert-${Date.now() - Math.random() * 50000}`,
          deviceId: `dev-${Math.floor(Math.random() * 10).toString().padStart(3, '0')}`,
          deviceName: `Device ${Math.floor(Math.random() * 50)}`,
          severity: ['Critical', 'High', 'Medium'][Math.floor(Math.random() * 3)] as any,
          type: ['battery', 'storage', 'connectivity', 'compliance'][Math.floor(Math.random() * 4)] as any,
          message: 'Alert status updated',
          timestamp: new Date().toISOString(),
          createdAt: new Date().toISOString(),
          resolved: Math.random() > 0.5,
        };

        this.callbacks.onAlertUpdated?.(updatedAlert);
      }
    }, 8000); // Check every 8 seconds

    // Simulate periodic policy compliance changes
    this.mockPolicyInterval = setInterval(() => {
      if (Math.random() > 0.8) { // 20% chance
        const policyId = `policy-${Math.floor(Math.random() * 10)}`;
        const compliancePercentage = Math.floor(Math.random() * 30) + 70;
        this.callbacks.onPolicyComplianceChanged?.(policyId, compliancePercentage);
      }

      // Simulate policy deployment
      if (Math.random() > 0.9) { // 10% chance
        const policyId = `policy-${Math.floor(Math.random() * 10)}`;
        const deviceIds = Array.from({ length: Math.floor(Math.random() * 10) + 1 }, (_, i) =>
          `dev-${(i + 1).toString().padStart(3, '0')}`
        );
        this.callbacks.onPolicyDeployed?.(policyId, deviceIds);
      }
    }, 12000); // Check every 12 seconds

    // Cleanup interval reference (store it)
    if (!this.mockDataInterval) {
      this.mockDataInterval = alertInterval;
    }
  }

  /**
   * Subscribe to device-specific updates (telemetry, status changes)
   * Sends a subscription request to the server via SignalR
   */
  async subscribeToDevice(deviceId: string): Promise<void> {
    if (this.subscribedDeviceIds.has(deviceId)) {
      return; // Already subscribed
    }

    this.subscribedDeviceIds.add(deviceId);
    console.log(`📡 Subscribing to device ${deviceId} updates`);

    if (this.useMockData) {
      // Start mock telemetry stream for this device
      this.startMockDeviceTelemetry(deviceId);
    } else if (this.connection?.state === HubConnectionState.Connected) {
      try {
        await this.connection.invoke('SubscribeToDevice', deviceId);
      } catch (error) {
        console.error(`Failed to subscribe to device ${deviceId}:`, error);
        this.subscribedDeviceIds.delete(deviceId);
      }
    }
  }

  /**
   * Unsubscribe from device-specific updates
   */
  async unsubscribeFromDevice(deviceId: string): Promise<void> {
    if (!this.subscribedDeviceIds.has(deviceId)) {
      return;
    }

    this.subscribedDeviceIds.delete(deviceId);
    console.log(`📡 Unsubscribing from device ${deviceId} updates`);

    if (this.useMockData) {
      // Stop mock telemetry stream
      const interval = this.mockTelemetryIntervals.get(deviceId);
      if (interval) {
        clearInterval(interval);
        this.mockTelemetryIntervals.delete(deviceId);
      }
    } else if (this.connection?.state === HubConnectionState.Connected) {
      try {
        await this.connection.invoke('UnsubscribeFromDevice', deviceId);
      } catch (error) {
        console.error(`Failed to unsubscribe from device ${deviceId}:`, error);
      }
    }
  }

  /**
   * Subscribe to real-time alert updates
   */
  async subscribeToAlerts(): Promise<void> {
    console.log('📡 Subscribing to alert updates');

    if (this.useMockData) {
      // Mock alert events already started in startMockHub
      return;
    }

    if (this.connection?.state === HubConnectionState.Connected) {
      try {
        await this.connection.invoke('SubscribeToAlerts');
      } catch (error) {
        console.error('Failed to subscribe to alerts:', error);
      }
    }
  }

  /**
   * Unsubscribe from alert updates
   */
  async unsubscribeFromAlerts(): Promise<void> {
    console.log('📡 Unsubscribing from alert updates');

    if (this.useMockData) {
      return;
    }

    if (this.connection?.state === HubConnectionState.Connected) {
      try {
        await this.connection.invoke('UnsubscribeFromAlerts');
      } catch (error) {
        console.error('Failed to unsubscribe from alerts:', error);
      }
    }
  }

  /**
   * Subscribe to real-time policy updates
   */
  async subscribeToPolicies(): Promise<void> {
    console.log('📡 Subscribing to policy updates');

    if (this.useMockData) {
      // Mock policy events will be simulated
      return;
    }

    if (this.connection?.state === HubConnectionState.Connected) {
      try {
        await this.connection.invoke('SubscribeToPolicies');
      } catch (error) {
        console.error('Failed to subscribe to policies:', error);
      }
    }
  }

  /**
   * Unsubscribe from policy updates
   */
  async unsubscribeFromPolicies(): Promise<void> {
    console.log('📡 Unsubscribing from policy updates');

    if (this.useMockData) {
      return;
    }

    if (this.connection?.state === HubConnectionState.Connected) {
      try {
        await this.connection.invoke('UnsubscribFromPolicies');
      } catch (error) {
        console.error('Failed to unsubscribe from policies:', error);
      }
    }
  }

  /**
   * Start mock telemetry data stream for a device
   */
  private startMockDeviceTelemetry(deviceId: string): void {
    if (this.mockTelemetryIntervals.has(deviceId)) {
      return; // Already streaming
    }

    const interval = setInterval(() => {
      const storageUsageMB = Math.floor(Math.random() * 40000) + 10000;
      const memoryUsageMB = Math.floor(Math.random() * 3000) + 1000;
      const mockTelemetry: DeviceTelemetry = {
        deviceId,
        batteryPercentage: Math.max(5, Math.floor(Math.random() * 100 - Math.random() * 10)),
        batteryHealth: ['Good', 'Fair', 'Poor'][Math.floor(Math.random() * 3)],
        batteryStatus: 'Discharging',
        storageUsageMB,
        totalStorageMB: 64000,
        storageAvailableMB: Math.max(0, 64000 - storageUsageMB),
        memoryUsageMB,
        totalMemoryMB: 8000,
        memoryAvailableMB: Math.max(0, 8000 - memoryUsageMB),
        cpuUsagePercentage: Math.floor(Math.random() * 80),
        temperature: Math.floor(Math.random() * 20) + 30,
        signalStrength: Math.floor(Math.random() * -30) - 50,
        wifiSignalStrength: -60,
        cellularSignalStrength: -70,
        connectivity: 'WiFi',
        timestamp: new Date().toISOString(),
      };

      this.callbacks.onDeviceTelemetryUpdate?.(deviceId, mockTelemetry);

      // Occasionally simulate online/offline status changes
      if (Math.random() > 0.95) {
        const isOnline = Math.random() > 0.5;
        this.callbacks.onDeviceOnlineStatusChanged?.(deviceId, isOnline);
      }
    }, 5000); // Every 5 seconds

    this.mockTelemetryIntervals.set(deviceId, interval);
  }

  /**
   * Re-subscribe to all devices after reconnection
   */
  private async resubscribeDevices(): Promise<void> {
    const deviceIds = Array.from(this.subscribedDeviceIds);
    console.log(`🔄 Re-subscribing to ${deviceIds.length} devices after reconnection`);

    for (const deviceId of deviceIds) {
      if (!this.useMockData && this.connection?.state === HubConnectionState.Connected) {
        try {
          await this.connection.invoke('SubscribeToDevice', deviceId);
        } catch (error) {
          console.error(`Failed to re-subscribe to device ${deviceId}:`, error);
        }
      }
    }
  }

  private reconnectWithBackoff(): void {
    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      console.error('❌ SignalR: Max reconnection attempts reached, falling back to mock hub');
      this.useMockData = true;
      this.startMockHub();
      return;
    }

    this.reconnectAttempts++;
    const delay = this.reconnectDelay * Math.pow(2, this.reconnectAttempts - 1);
    console.log(`🔄 SignalR: Reconnecting in ${delay}ms (attempt ${this.reconnectAttempts}/${this.maxReconnectAttempts})`);

    setTimeout(() => {
      this.connect(this.callbacks);
    }, delay);
  }

  private setConnectionState(state: ConnectionState): void {
    this.connectionState = state;
    this.callbacks.onConnectionStateChanged?.(state);
  }

  getConnectionState(): ConnectionState {
    if (this.connection) {
      switch (this.connection.state) {
        case HubConnectionState.Connected:
          return 'connected';
        case HubConnectionState.Connecting:
          return 'connecting';
        case HubConnectionState.Disconnected:
          return 'disconnected';
        case HubConnectionState.Reconnecting:
          return 'connecting';
      }
    }
    return this.connectionState;
  }

  isConnected(): boolean {
    return this.getConnectionState() === 'connected';
  }

  async disconnect(): Promise<void> {
    if (this.mockDataInterval) {
      clearInterval(this.mockDataInterval);
      this.mockDataInterval = null;
    }
    if (this.mockPolicyInterval) {
      clearInterval(this.mockPolicyInterval);
      this.mockPolicyInterval = null;
    }

    // Clean up all device telemetry streams
    this.mockTelemetryIntervals.forEach((interval) => {
      clearInterval(interval);
    });
    this.mockTelemetryIntervals.clear();
    this.subscribedDeviceIds.clear();

    if (this.connection?.state === HubConnectionState.Connected) {
      try {
        await this.connection.stop();
      } catch (error) {
        console.error('Error stopping connection:', error);
      }
    }

    this.setConnectionState('disconnected');
    console.log('✅ SignalR: Disconnected');
  }

  // Allow manual trigger of mock events for testing
  triggerMockFleetMetricsUpdate(metrics: FleetHealthMetrics): void {
    this.callbacks.onFleetMetricsUpdate?.(metrics);
  }

  triggerMockAlert(alert: HealthAlert): void {
    this.callbacks.onAlertReceived?.(alert);
  }

  triggerMockDeviceStatusChange(device: Device): void {
    this.callbacks.onDeviceStatusChanged?.(device);
  }

  triggerMockDeviceTelemetry(deviceId: string, telemetry: DeviceTelemetry): void {
    this.callbacks.onDeviceTelemetryUpdate?.(deviceId, telemetry);
  }

  triggerMockDeviceOnlineStatusChange(deviceId: string, isOnline: boolean): void {
    this.callbacks.onDeviceOnlineStatusChanged?.(deviceId, isOnline);
  }

  triggerMockAlertUpdated(alert: HealthAlert): void {
    this.callbacks.onAlertUpdated?.(alert);
  }

  triggerMockAlertResolved(alertId: string): void {
    this.callbacks.onAlertResolved?.(alertId);
  }

  triggerMockPolicyUpdated(policy: Policy): void {
    this.callbacks.onPolicyUpdated?.(policy);
  }

  triggerMockPolicyDeployed(policyId: string, deviceIds: string[]): void {
    this.callbacks.onPolicyDeployed?.(policyId, deviceIds);
  }

  triggerMockPolicyComplianceChanged(policyId: string, compliancePercentage: number): void {
    this.callbacks.onPolicyComplianceChanged?.(policyId, compliancePercentage);
  }
}

// Export singleton instance
export default new SignalRService();
