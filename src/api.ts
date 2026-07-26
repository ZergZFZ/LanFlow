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

export const getItemIcon = (path: string) =>
  invoke<string | null>("get_item_icon", { path });

export const openItem = (path: string) =>
  invoke<void>("open_item", { path });

export const removeItem = (groupId: string, itemId: string) =>
  invoke<AppConfig>("remove_item", { groupId, itemId });

export const updateItem = (groupId: string, itemId: string, name: string, path: string) =>
  invoke<AppConfig>("update_item", { groupId, itemId, name, path });

export const moveItem = (fromGroupId: string, toGroupId: string, itemId: string) =>
  invoke<AppConfig>("move_item", { fromGroupId, toGroupId, itemId });

export const reorderGroup = (groupId: string, targetIndex: number) =>
  invoke<AppConfig>("reorder_group", { groupId, targetIndex });

export const reorderItem = (groupId: string, itemId: string, targetIndex: number) =>
  invoke<AppConfig>("reorder_item", { groupId, itemId, targetIndex });

export const hideLauncherWindow = () => invoke<void>("hide_launcher_window");

export const setEditMode = (enabled: boolean) =>
  invoke<void>("set_edit_mode", { enabled });

export const updateSettings = (settings: Settings) =>
  invoke<AppConfig>("update_settings", { settings });

export const updateHotkey = (hotkey: string) =>
  invoke<AppConfig>("update_hotkey", { hotkey });

export const search = (query: string) =>
  invoke<SearchResult[]>("search", { query });
