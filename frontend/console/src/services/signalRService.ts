import { HubConnectionBuilder, HubConnectionState, HubConnection } from '@microsoft/signalr';
import type { Device, HealthAlert, FleetHealthMetrics } from '@types';

export type ConnectionState = 'connected' | 'connecting' | 'disconnected' | 'failed';

export interface SignalRServiceCallbacks {
  onFleetMetricsUpdate?: (metrics: FleetHealthMetrics) => void;
  onAlertReceived?: (alert: HealthAlert) => void;
  onDeviceStatusChanged?: (device: Device) => void;
  onConnectionStateChanged?: (state: ConnectionState) => void;
}

class SignalRService {
  private connection: HubConnection | null = null;
  private connectionState: ConnectionState = 'disconnected';
  private reconnectAttempts = 0;
  private maxReconnectAttempts = 5;
  private reconnectDelay = 1000; // 1 second
  private useMockData = true; // Use mock hub when backend not available
  private mockDataInterval: ReturnType<typeof setInterval> | null = null;
  private callbacks: SignalRServiceCallbacks = {};

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
        .withUrl('/signalr', {
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

      // Connection lifecycle events
      this.connection.onreconnecting(() => {
        console.log('🔄 SignalR: Attempting to reconnect...');
        this.setConnectionState('connecting');
      });

      this.connection.onreconnected(() => {
        console.log('✅ SignalR: Reconnected successfully');
        this.reconnectAttempts = 0;
        this.setConnectionState('connected');
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
}

// Export singleton instance
export default new SignalRService();
