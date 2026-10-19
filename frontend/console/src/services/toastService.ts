import { ref } from 'vue';

export type ToastType = 'success' | 'error' | 'info' | 'warning';

export interface Toast {
  id: string;
  message: string;
  type: ToastType;
  duration: number;
  createdAt: Date;
}

// Global toast state
const toasts = ref<Toast[]>([]);

class ToastService {
  show(message: string, type: ToastType = 'info', duration: number = 3000): string {
    const id = `toast-${Date.now()}-${Math.random()}`;
    
    const toast: Toast = {
      id,
      message,
      type,
      duration,
      createdAt: new Date(),
    };

    toasts.value.push(toast);

    // Auto-remove after duration
    if (duration > 0) {
      setTimeout(() => {
        this.remove(id);
      }, duration);
    }

    return id;
  }

  success(message: string, duration?: number): string {
    return this.show(message, 'success', duration);
  }

  error(message: string, duration?: number): string {
    return this.show(message, 'error', duration ?? 5000);
  }

  info(message: string, duration?: number): string {
    return this.show(message, 'info', duration);
  }

  warning(message: string, duration?: number): string {
    return this.show(message, 'warning', duration ?? 4000);
  }

  remove(id: string): void {
    const index = toasts.value.findIndex(t => t.id === id);
    if (index > -1) {
      toasts.value.splice(index, 1);
    }
  }

  clear(): void {
    toasts.value = [];
  }

  getToasts() {
    return toasts;
  }
}

export default new ToastService();
