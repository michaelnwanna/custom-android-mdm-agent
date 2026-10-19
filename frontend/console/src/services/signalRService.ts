import { HubConnectionBuilder, HubConnectionState, HubConnection } from '@microsoft/signalr';
import type { Device, HealthAlert, FleetHealthMetrics, DeviceTelemetry } from '@types';

export type ConnectionState = 'connected' | 'connecting' | 'disconnected' | 'failed';

export interface SignalRServiceCallbacks {
  onFleetMetricsUpdate?: (metrics: FleetHealthMetrics) => void;
  onAlertReceived?: (alert: HealthAlert) => void;
  onDeviceStatusChanged?: (device: Device) => void;
  onDeviceTelemetryUpdate?: (deviceId: string, telemetry: DeviceTelemetry) => void;
  onConnectionStateChanged?: (state: ConnectionState) => void;
  onDeviceOnlineStatusChanged?: (deviceId: string, isOnline: boolean) => void;
}

class SignalRService {
  private connection: HubConnection | null = null;
  private connectionState: ConnectionState = 'disconnected';
  private reconnectAttempts = 0;
  private maxReconnectAttempts = 5;
  private reconnectDelay = 1000; // 1 second
  private useMockData = true; // Use mock hub when backend not available
  private mockDataInterval: ReturnType<typeof setInterval> | null = null;
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

    // Simulate periodic alert arrivals
    const alertInterval = setInterval(() => {
      if (Math.random() > 0.7) { // 30% chance
        const mockAlert: HealthAlert = {
          id: `alert-${Date.now()}`,
          deviceId: `dev-${Math.floor(Math.random() * 10).toString().padStart(3, '0')}`,
          deviceName: `Device ${Math.floor(Math.random() * 50)}`,
          severity: ['critical', 'high', 'medium'][Math.floor(Math.random() * 3)] as any,
          type: ['battery', 'storage', 'connectivity', 'compliance'][Math.floor(Math.random() * 4)] as any,
          message: 'Mock alert from SignalR',
          timestamp: new Date().toISOString(),
          resolved: false,
        };

        this.callbacks.onAlertReceived?.(mockAlert);
      }
    }, 10000); // Check every 10 seconds

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
   * Start mock telemetry data stream for a device
   */
  private startMockDeviceTelemetry(deviceId: string): void {
    if (this.mockTelemetryIntervals.has(deviceId)) {
      return; // Already streaming
    }

    const interval = setInterval(() => {
      const mockTelemetry: DeviceTelemetry = {
        deviceId,
        batteryPercentage: Math.max(5, Math.floor(Math.random() * 100 - Math.random() * 10)),
        batteryHealth: ['Good', 'Fair', 'Poor'][Math.floor(Math.random() * 3)],
        storageUsageMB: Math.floor(Math.random() * 40000) + 10000,
        totalStorageMB: 64000,
        memoryUsageMB: Math.floor(Math.random() * 3000) + 1000,
        totalMemoryMB: 8000,
        cpuUsagePercentage: Math.floor(Math.random() * 80),
        temperature: Math.floor(Math.random() * 20) + 30,
        signalStrength: Math.floor(Math.random() * -30) - 50,
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
}

// Export singleton instance
export default new SignalRService();
