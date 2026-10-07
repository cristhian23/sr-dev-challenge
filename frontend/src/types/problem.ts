export interface Problem {
  title?: string;
  detail?: string;
  codigo?: string;
  errors?: Record<string, string[]>;
}
