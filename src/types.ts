export interface Item {
  id: string;
  name: string;
  path: string;
  icon?: string | null;
}

export interface Group {
  id: string;
  name: string;
  collapsed: boolean;
  items: Item[];
}

export interface Settings {
  hotkey: string;
  theme: string;
  opacity: number;
}

export interface AppConfig {
  groups: Group[];
  settings: Settings;
}

export interface SearchResult {
  group_id: string;
  group_name: string;
  item: Item;
}
