import { invoke } from "@tauri-apps/api/core";
import type { AppConfig, SearchResult, Settings } from "./types";

export const loadConfig = () => invoke<AppConfig>("load_config");

export const saveConfig = (config: AppConfig) =>
  invoke<void>("save_config", { config });

export const addGroup = (name: string) =>
  invoke<AppConfig>("add_group", { name });

export const removeGroup = (groupId: string) =>
  invoke<AppConfig>("remove_group", { group_id: groupId });

export const renameGroup = (groupId: string, name: string) =>
  invoke<AppConfig>("rename_group", { group_id: groupId, name });

export const addItem = (groupId: string, name: string, path: string) =>
  invoke<AppConfig>("add_item", { group_id: groupId, name, path });

export const removeItem = (groupId: string, itemId: string) =>
  invoke<AppConfig>("remove_item", { group_id: groupId, item_id: itemId });

export const updateSettings = (settings: Settings) =>
  invoke<AppConfig>("update_settings", { settings });

export const search = (query: string) =>
  invoke<SearchResult[]>("search", { query });
