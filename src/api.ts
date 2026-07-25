import { invoke } from "@tauri-apps/api/core";
import type { AppConfig, SearchResult, Settings } from "./types";

export const loadConfig = () => invoke<AppConfig>("load_config");

export const saveConfig = (config: AppConfig) =>
  invoke<void>("save_config", { config });

export const addGroup = (name: string) =>
  invoke<AppConfig>("add_group", { name });

export const removeGroup = (groupId: string) =>
  invoke<AppConfig>("remove_group", { groupId });

export const renameGroup = (groupId: string, name: string) =>
  invoke<AppConfig>("rename_group", { groupId, name });

export const addItem = (groupId: string, name: string, path: string) =>
  invoke<AppConfig>("add_item", { groupId, name, path });

export const removeItem = (groupId: string, itemId: string) =>
  invoke<AppConfig>("remove_item", { groupId, itemId });

export const updateSettings = (settings: Settings) =>
  invoke<AppConfig>("update_settings", { settings });

export const search = (query: string) =>
  invoke<SearchResult[]>("search", { query });
