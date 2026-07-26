import { useCallback, useEffect, useRef, useState } from "react";
import { listen, TauriEvent } from "@tauri-apps/api/event";

import * as api from "./api";
import type { AppConfig, Group, Item, SearchResult } from "./types";

const DEFAULT_CONFIG: AppConfig = {
  groups: [],
  settings: { hotkey: "Alt+Space", theme: "dark", opacity: 1 },
};

type OperationDialog =
  | { type: "group"; group?: Group }
  | { type: "item"; group: Group; item?: Item }
  | { type: "confirm"; title: string; message: string; confirmLabel: string; onConfirm: () => Promise<void> };

function ItemIcon({ item }: { item: Item }) {
  const [icon, setIcon] = useState(item.icon ?? null);
  const extension = item.path.split(".").pop()?.toLowerCase();

  useEffect(() => {
    let cancelled = false;
    setIcon(item.icon ?? null);
    if (item.icon) return;

    void api.getItemIcon(item.path).then((value) => {
      if (!cancelled) setIcon(value);
    });
    return () => {
      cancelled = true;
    };
  }, [item.icon, item.path]);

  if (icon) {
    return <img className="tile-icon native-icon" src={icon} alt="" />;
  }

  return <div className={`tile-icon file-icon file-icon-${extension ?? "file"}`}>{extension?.slice(0, 3).toUpperCase() ?? "FILE"}</div>;
}

export default function App() {
  const [config, setConfig] = useState<AppConfig>(DEFAULT_CONFIG);
  const [activeId, setActiveId] = useState("");
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResult[]>([]);
  const [dragging, setDragging] = useState(false);
  const [isEditMode, setIsEditMode] = useState(false);
  const [draggedItem, setDraggedItem] = useState<{ item: Item; groupId: string } | null>(null);
  const [dragTargetId, setDragTargetId] = useState<string | null>(null);
  const [dragOverItemId, setDragOverItemId] = useState<string | null>(null);
  const [draggedGroupId, setDraggedGroupId] = useState<string | null>(null);
  const [groupDropTarget, setGroupDropTarget] = useState<{ id: string; after: boolean } | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [showSettings, setShowSettings] = useState(false);

  const [hotkeyDraft, setHotkeyDraft] = useState(DEFAULT_CONFIG.settings.hotkey);
  const [windowMotion, setWindowMotion] = useState<"visible" | "entering">("visible");
  const [contextItem, setContextItem] = useState<{ item: Item; group: Group; x: number; y: number } | null>(null);
  const [operationDialog, setOperationDialog] = useState<OperationDialog | null>(null);
  const [operationName, setOperationName] = useState("");
  const [operationPath, setOperationPath] = useState("");
  const searchInputRef = useRef<HTMLInputElement>(null);


  const showStatus = useCallback((msg: string) => {
    setStatus(msg);
    setTimeout(() => setStatus(null), 4000);
  }, []);

  const applyTheme = useCallback((theme: string, opacity: number) => {
    const root = document.documentElement;
    root.setAttribute("data-theme", theme);
    root.style.setProperty("--opacity", String(opacity));
  }, []);

  const refresh = useCallback(async () => {
    const cfg = await api.loadConfig();
    setConfig(cfg);
    setHotkeyDraft(cfg.settings.hotkey);
    if (!activeId && cfg.groups.length) setActiveId(cfg.groups[0].id);
    applyTheme(cfg.settings.theme, cfg.settings.opacity);
  }, [activeId, applyTheme]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  useEffect(() => {
    let disposed = false;
    const unlisteners: Array<() => void> = [];

    const register = (event: "launcher-show" | "launcher-hide-request", handler: () => void) => {
      void listen(event, handler).then((unlisten) => {
        if (disposed) unlisten();
        else unlisteners.push(unlisten);
      });
    };

    register("launcher-show", () => {
      setQuery("");
      setContextItem(null);
      setShowSettings(false);
      setWindowMotion("entering");
      window.requestAnimationFrame(() => {
        setWindowMotion("visible");
        window.setTimeout(() => searchInputRef.current?.focus(), 0);
      });
    });

    register("launcher-hide-request", () => {
      setWindowMotion("visible");
      void api.hideLauncherWindow();
    });

    return () => {
      disposed = true;
      unlisteners.forEach((unlisten) => unlisten());
    };
  }, []);


  useEffect(() => {
    if (!query.trim()) {
      setResults([]);
      return;
    }
    let cancelled = false;
    api.search(query).then((r) => {
      if (!cancelled) setResults(r);
    });
    return () => {
      cancelled = true;
    };
  }, [query]);

  const configRef = useRef(config);
  const activeIdRef = useRef(activeId);

  useEffect(() => {
    configRef.current = config;
  }, [config]);

  useEffect(() => {
    activeIdRef.current = activeId;
  }, [activeId]);

  // 从资源管理器拖拽文件/快捷方式到窗口 -> 加入当前分组
  useEffect(() => {
    let disposed = false;
    const unsubs: Array<() => void> = [];
    const register = <T,>(event: TauriEvent, handler: (event: { payload: T }) => void) => {
      void listen<T>(event, handler).then((unlisten) => {
        if (disposed) unlisten();
        else unsubs.push(unlisten);
      });
    };

    const onEnter = () => setDragging(true);
    const onLeave = () => setDragging(false);
    register(TauriEvent.DRAG_ENTER, onEnter);
    register(TauriEvent.DRAG_OVER, onEnter);
    register(TauriEvent.DRAG_LEAVE, onLeave);
    register<{ paths?: string[] }>(TauriEvent.DRAG_DROP, (e) => {
      setDragging(false);
      const paths = e.payload.paths;
      if (!paths?.length) {
        showStatus("拖放事件未携带路径");
        return;
      }

      const targetGroup = activeIdRef.current || configRef.current.groups[0]?.id;
      if (!targetGroup) {
        showStatus("请先创建并选中一个分组");
        return;
      }

      void (async () => {
        try {
          let cfg: AppConfig | null = null;
          for (const path of paths) {
            const name = path.split(/[\\/]/).pop() ?? path;
            cfg = await api.addItem(targetGroup, name, path);
          }
          if (cfg) setConfig(cfg);
          showStatus(`已添加 ${paths.length} 个快捷方式`);
        } catch (err) {
          showStatus("添加失败：" + String(err));
        }
      })();
    });

    return () => {
      disposed = true;
      unsubs.forEach((unlisten) => unlisten());
    };
  }, [showStatus]);

  const handleEditModeToggle = () => {
    const nextEditing = !isEditMode;

    setIsEditMode(nextEditing);
    void api.setEditMode(nextEditing).catch((err) => showStatus(`切换编辑模式失败：${String(err)}`));
    if (nextEditing) {
      setQuery("");
      setContextItem(null);
      setShowSettings(false);
    }
    setDraggedItem(null);
    setDragTargetId(null);
    setDragOverItemId(null);
  };

  const handleAddGroup = () => {
    setOperationName("新分组");
    setOperationPath("");
    setOperationDialog({ type: "group" });
  };

  const handleDeleteGroup = (group: Group) => {
    setOperationDialog({
      type: "confirm",
      title: "删除分组",
      message: `确定删除“${group.name}”及其所有图标吗？`,
      confirmLabel: "删除",
      onConfirm: async () => {
        const cfg = await api.removeGroup(group.id);
        setConfig(cfg);
        if (activeId === group.id) setActiveId(cfg.groups[0]?.id ?? "");
        showStatus("分组已删除");
      },
    });
  };

  const handleRename = (group: Group) => {
    setOperationName(group.name);
    setOperationPath("");
    setOperationDialog({ type: "group", group });
  };



  const handleLaunch = async (it: Item) => {
    if (isEditMode) return;

    try {
      await api.openItem(it.path);
      setQuery("");
      setContextItem(null);
      setShowSettings(false);
      void api.hideLauncherWindow();
    } catch (error) {
      showStatus(`打开失败：${String(error)}`);
    }
  };

  useEffect(() => {
    if (!contextItem) return;
    const dismiss = () => setContextItem(null);
    window.addEventListener("click", dismiss);
    return () => window.removeEventListener("click", dismiss);
  }, [contextItem]);

  const handleEditItem = (group: Group, item: Item) => {
    setContextItem(null);
    setOperationName(item.name);
    setOperationPath(item.path);
    setOperationDialog({ type: "item", group, item });
  };

  const handleMoveItem = async (group: Group, item: Item, targetGroupId: string) => {
    setContextItem(null);
    try {
      const cfg = await api.moveItem(group.id, targetGroupId, item.id);
      setConfig(cfg);
      setActiveId(targetGroupId);
      showStatus("已移动到目标分组");
    } catch (error) {
      showStatus(`移动失败：${String(error)}`);
    }
  };

  const handleGroupDragStart = (event: React.DragEvent<HTMLDivElement>, groupId: string) => {
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", groupId);
    setDraggedGroupId(groupId);
  };

  const handleGroupDragOver = (event: React.DragEvent<HTMLDivElement>, targetGroup: Group) => {
    if (!draggedGroupId || draggedGroupId === targetGroup.id) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    const { top, height } = event.currentTarget.getBoundingClientRect();
    setGroupDropTarget({ id: targetGroup.id, after: event.clientY > top + height / 2 });
  };

  const handleGroupDrop = (event: React.DragEvent<HTMLDivElement>, targetGroup: Group) => {
    event.preventDefault();
    event.stopPropagation();
    const sourceId = draggedGroupId;
    setDraggedGroupId(null);
    setGroupDropTarget(null);
    if (!sourceId || sourceId === targetGroup.id) return;

    const sourceIndex = config.groups.findIndex((group) => group.id === sourceId);
    const targetIndex = config.groups.findIndex((group) => group.id === targetGroup.id);
    if (sourceIndex < 0 || targetIndex < 0) return;
    const { top, height } = event.currentTarget.getBoundingClientRect();
    const insertAfter = event.clientY > top + height / 2;
    let destinationIndex = targetIndex + (insertAfter ? 1 : 0);
    if (sourceIndex < destinationIndex) destinationIndex -= 1;
    void handleReorderGroup(sourceId, destinationIndex);
  };

  const handleReorderGroup = async (groupId: string, targetIndex: number) => {
    try {
      setConfig(await api.reorderGroup(groupId, targetIndex));
    } catch (error) {
      showStatus(`分组排序失败：${String(error)}`);
    }
  };

  const handleItemDragStart = (event: React.DragEvent<HTMLDivElement>, group: Group, item: Item) => {
    event.stopPropagation();
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("application/x-lanflow-item", JSON.stringify({ groupId: group.id, itemId: item.id }));
    event.dataTransfer.setData("text/plain", item.id);
    setDraggedItem({ item, groupId: group.id });
  };

  const handleItemDrop = (event: React.DragEvent<HTMLElement>, targetGroup: Group) => {
    event.preventDefault();
    const source = draggedItem;
    setDragTargetId(null);
    setDragOverItemId(null);
    setDraggedItem(null);
    if (!source) return;

    if (source.groupId !== targetGroup.id) {
      const sourceGroup = config.groups.find((group) => group.id === source.groupId);
      if (sourceGroup) void handleMoveItem(sourceGroup, source.item, targetGroup.id);
      return;
    }

    const sourceIndex = targetGroup.items.findIndex((item) => item.id === source.item.id);
    if (sourceIndex < 0) return;
    void handleReorderItem(targetGroup, source.item.id, targetGroup.items.length - 1);
  };

  const handleItemDragOver = (
    event: React.DragEvent<HTMLDivElement>,
    targetGroup: Group,
    targetItem: Item,
  ) => {
    if (!draggedItem || draggedItem.groupId !== targetGroup.id || draggedItem.item.id === targetItem.id) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    setDragOverItemId(targetItem.id);
  };

  const handleItemDropOnTile = (
    event: React.DragEvent<HTMLDivElement>,
    targetGroup: Group,
    targetItem: Item,
  ) => {
    event.preventDefault();
    event.stopPropagation();
    const source = draggedItem;
    setDragTargetId(null);
    setDragOverItemId(null);
    setDraggedItem(null);
    if (!source) return;

    if (source.groupId !== targetGroup.id) {
      const sourceGroup = config.groups.find((group) => group.id === source.groupId);
      if (sourceGroup) void handleMoveItem(sourceGroup, source.item, targetGroup.id);
      return;
    }

    const sourceIndex = targetGroup.items.findIndex((item) => item.id === source.item.id);
    const targetIndex = targetGroup.items.findIndex((item) => item.id === targetItem.id);
    if (sourceIndex < 0 || targetIndex < 0 || sourceIndex === targetIndex) return;

    const insertAfter = event.clientX > event.currentTarget.getBoundingClientRect().left + event.currentTarget.getBoundingClientRect().width / 2;
    let destinationIndex = targetIndex + (insertAfter ? 1 : 0);
    if (sourceIndex < destinationIndex) destinationIndex -= 1;
    void handleReorderItem(targetGroup, source.item.id, destinationIndex);
  };

  const handleReorderItem = async (group: Group, itemId: string, targetIndex: number) => {
    try {
      const cfg = await api.reorderItem(group.id, itemId, targetIndex);
      setConfig(cfg);
    } catch (error) {
      showStatus(`排序失败：${String(error)}`);
    }
  };

  const handleDeleteItem = (group: Group, itemId: string) => {
    setContextItem(null);
    setOperationDialog({
      type: "confirm",
      title: "移除启动项",
      message: "确定从当前分组移除此启动项吗？",
      confirmLabel: "移除",
      onConfirm: async () => {
        setConfig(await api.removeItem(group.id, itemId));
        showStatus("启动项已移除");
      },
    });
  };

  const handleThemeToggle = async () => {
    const theme = config.settings.theme === "dark" ? "light" : "dark";
    const settings = { ...config.settings, theme };
    const cfg = await api.updateSettings(settings);
    setConfig(cfg);
    applyTheme(theme, cfg.settings.opacity);
  };

  const handleOpacity = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const opacity = Number(e.target.value);
    const settings = { ...config.settings, opacity };
    const cfg = await api.updateSettings(settings);
    setConfig(cfg);
    applyTheme(cfg.settings.theme, opacity);
  };

  const handleUpdateHotkey = async () => {
    try {
      const cfg = await api.updateHotkey(hotkeyDraft);
      setConfig(cfg);
      setHotkeyDraft(cfg.settings.hotkey);
      showStatus(`已设为 ${cfg.settings.hotkey}`);
    } catch (error) {
      showStatus(`快捷键更新失败：${String(error)}`);
    }
  };

  const handleSaveOperation = async () => {
    if (!operationDialog || operationDialog.type === "confirm") return;
    const name = operationName.trim();
    const path = operationPath.trim();
    if (!name || (operationDialog.type === "item" && !path)) {
      showStatus(operationDialog.type === "item" ? "请填写名称和启动路径" : "请填写分组名称");
      return;
    }

    try {
      let cfg: AppConfig;
      if (operationDialog.type === "group") {
        if (operationDialog.group) {
          cfg = await api.renameGroup(operationDialog.group.id, name);
        } else {
          cfg = await api.addGroup(name);
          setActiveId(cfg.groups[cfg.groups.length - 1]?.id ?? "");
        }
      } else if (operationDialog.item) {
        cfg = await api.updateItem(operationDialog.group.id, operationDialog.item.id, name, path);
      } else {
        cfg = await api.addItem(operationDialog.group.id, name, path);
      }
      setConfig(cfg);
      setOperationDialog(null);
      showStatus("已保存");
    } catch (error) {
      showStatus(`保存失败：${String(error)}`);
    }
  };

  const handleConfirmOperation = async () => {
    if (!operationDialog || operationDialog.type !== "confirm") return;
    try {
      await operationDialog.onConfirm();
      setOperationDialog(null);
    } catch (error) {
      showStatus(`操作失败：${String(error)}`);
    }
  };

  const searching = query.trim().length > 0;
  const activeGroup = config.groups.find((g) => g.id === activeId);

  return (
    <div
      className={`app window-${windowMotion}${dragging ? " dragging" : ""}${isEditMode ? " editing" : ""}`}
      onDragOver={(e) => e.preventDefault()}
      onDrop={(e) => e.preventDefault()}
    >
      <header className="toolbar">
        <input
          className="search-input"
          ref={searchInputRef}
          placeholder={`搜索应用…（${config.settings.hotkey} 呼出）`}
          value={query}
          disabled={isEditMode}
          onChange={(e) => setQuery(e.target.value)}
        />
        <div className="header-actions">
          <button
            className={`icon-button ${isEditMode ? "edit-mode-active" : ""}`}
            onClick={handleEditModeToggle}
            title={isEditMode ? "退出编辑" : "编辑模式"}
            aria-label={isEditMode ? "退出编辑" : "编辑模式"}
          >
            {isEditMode ? "完成" : "编辑"}
          </button>
          <button
            className="icon-button settings-button"
            onClick={() => !isEditMode && setShowSettings((visible) => !visible)}
            title="设置"
            aria-label="设置"
            disabled={isEditMode}
          >
            ⚙
          </button>
        </div>
      </header>

      {searching ? (
        <main className="grid">
          {results.length === 0 && <div className="empty">无匹配结果</div>}
          {results.map((r) => (
            <div
              key={r.item.id}
              className="tile"
              onClick={(event) => {
                if (event.detail === 1) void handleLaunch(r.item);
              }}
              title={`${r.item.path}（单击启动）`}
            >
              <ItemIcon item={r.item} />
              <span className="tile-name">{r.item.name}</span>
              <span className="tile-sub">{r.group_name}</span>
            </div>
          ))}
        </main>
      ) : (
        <div className="body">
          <aside className="groups">
            <div className="group-list">
              {config.groups.map((g) => (
                <div
                  key={g.id}
                  className={
                    "group-row" +
                    (g.id === activeId ? " active" : "") +
                    (g.id === dragTargetId ? " drop-target" : "") +
                    (groupDropTarget?.id === g.id ? (groupDropTarget.after ? " reorder-target-after" : " reorder-target-before") : "") +
                    (g.id === draggedGroupId ? " group-dragging" : "")
                  }
                  onMouseEnter={() => {
                    if (!draggedGroupId && !draggedItem) setActiveId(g.id);
                  }}
                  draggable={isEditMode}
                  onDragStart={(event) => {
                    if (isEditMode) handleGroupDragStart(event, g.id);
                  }}
                  onDragOver={(event) => {
                    if (draggedGroupId) {
                      handleGroupDragOver(event, g);
                      return;
                    }
                    if (!draggedItem || draggedItem.groupId === g.id) return;
                    event.preventDefault();
                    event.dataTransfer.dropEffect = "move";
                    setDragTargetId(g.id);
                  }}
                  onDragLeave={(event) => {
                    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
                      setDragTargetId(null);
                      setGroupDropTarget(null);
                    }
                  }}
                  onDrop={(event) => {
                    if (draggedGroupId) {
                      handleGroupDrop(event, g);
                      return;
                    }
                    handleItemDrop(event, g);
                  }}
                  onDragEnd={() => {
                    setDraggedGroupId(null);
                    setGroupDropTarget(null);
                    setDragTargetId(null);
                  }}
                >
                  <button
                    className="group-tab"
                    onClick={() => setActiveId(g.id)}
                    onDoubleClick={() => isEditMode && handleRename(g)}
                  >
                    {g.name}
                  </button>
                  {isEditMode && (
                    <span className="group-actions">
                      <button title="重命名分组" onClick={() => handleRename(g)} aria-label={`重命名${g.name}`}>
                        R
                      </button>
                      <button title="删除分组" onClick={() => handleDeleteGroup(g)} aria-label={`删除${g.name}`}>
                        X
                      </button>
                    </span>
                  )}
                </div>
              ))}
              {config.groups.length === 0 && <div className="empty">暂无分组</div>}
            </div>
            <div className="sidebar-footer">
              <div className="sidebar-edit-actions" aria-hidden={!isEditMode}>
                <button className="sidebar-action exit-edit" onClick={handleEditModeToggle} tabIndex={isEditMode ? 0 : -1}>退出编辑</button>
                <button className="sidebar-action" onClick={handleAddGroup} tabIndex={isEditMode ? 0 : -1}>新建分组</button>
              </div>
              <div className="sidebar-normal-actions" aria-hidden={isEditMode}>
                <button className="sidebar-action" onClick={handleEditModeToggle} tabIndex={isEditMode ? -1 : 0}>编辑模式</button>
                <button className="sidebar-action" onClick={() => setShowSettings(true)} tabIndex={isEditMode ? -1 : 0}>设置</button>
              </div>
            </div>
          </aside>
          <main
            className="grid"
            onDragOver={(event) => {
              if (draggedItem?.groupId === activeGroup?.id) event.preventDefault();
            }}
            onDrop={(event) => {
              if (activeGroup) handleItemDrop(event, activeGroup);
            }}
          >
            {activeGroup?.items.map((it) => (
              <div
                key={it.id}
                className={"tile" + (it.id === dragOverItemId ? " reorder-target" : "")}
                draggable={isEditMode}
                onDragStart={(event) => handleItemDragStart(event, activeGroup, it)}
                onDragOver={(event) => handleItemDragOver(event, activeGroup, it)}
                onDragLeave={(event) => {
                  if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragOverItemId(null);
                }}
                onDrop={(event) => handleItemDropOnTile(event, activeGroup, it)}
                onDragEnd={() => {
                  setDraggedItem(null);
                  setDragTargetId(null);
                  setDragOverItemId(null);
                }}
                onClick={(event) => {
                  if (event.detail === 1 && !isEditMode) void handleLaunch(it);
                }}
                onContextMenu={(event) => {
                  event.preventDefault();
                  if (!isEditMode) setContextItem({ item: it, group: activeGroup, x: event.clientX, y: event.clientY });
                }}
                title={isEditMode ? `${it.path}（拖拽排序，点击删除角标移除）` : `${it.path}（单击启动，拖拽排序，右键管理）`}
              >
                <button
                  className="tile-delete"
                  type="button"
                  title={`移除${it.name}`}
                  aria-label={`移除${it.name}`}
                  aria-hidden={!isEditMode}
                  tabIndex={isEditMode ? 0 : -1}
                  draggable={false}
                  onClick={(event) => {
                    event.stopPropagation();
                    handleDeleteItem(activeGroup, it.id);
                  }}
                >
                  ×
                </button>
                <ItemIcon item={it} />
                <span className="tile-name">{it.name}</span>
              </div>
            ))}
            {activeGroup && activeGroup.items.length === 0 && (
              <div className="empty">空分组，点击左侧「＋」添加图标</div>
            )}
          </main>
        </div>
      )}
      {showSettings && (
        <div className="settings-backdrop" onMouseDown={() => setShowSettings(false)}>
          <section className="settings-panel" onMouseDown={(event) => event.stopPropagation()} aria-label="设置">
            <div className="settings-header">
              <h2>设置</h2>
              <button className="settings-close" onClick={() => setShowSettings(false)} aria-label="关闭设置">×</button>
            </div>
            <div className="settings-row">
              <span>外观</span>
              <button className="btn" onClick={() => void handleThemeToggle()}>
                切换为{config.settings.theme === "dark" ? "浅色" : "深色"}主题
              </button>
            </div>
            <label className="settings-row hotkey-control">
              <span>全局快捷键</span>
              <input
                value={hotkeyDraft}
                onChange={(event) => setHotkeyDraft(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter") void handleUpdateHotkey();
                }}
                aria-label="全局快捷键"
                placeholder="Alt+Space"
              />
              <button className="btn" onClick={() => void handleUpdateHotkey()}>保存</button>
            </label>
            <label className="settings-row opacity">
              <span>窗口透明度</span>
              <input
                type="range"
                min={0.4}
                max={1}
                step={0.05}
                value={config.settings.opacity}
                onChange={handleOpacity}
              />
              <output>{Math.round(config.settings.opacity * 100)}%</output>
            </label>
          </section>
        </div>
      )}
      {operationDialog && (
        <div
          className="operation-backdrop"
          onMouseDown={() => setOperationDialog(null)}
          role="presentation"
        >
          <section
            className="operation-dialog"
            onMouseDown={(event) => event.stopPropagation()}
            role="dialog"
            aria-modal="true"
            aria-label={operationDialog.type === "confirm" ? operationDialog.title : operationDialog.group ? "重命名分组" : operationDialog.type === "item" ? operationDialog.item ? "编辑启动项" : "添加启动项" : "新建分组"}
          >
            <div className="operation-header">
              <h2>{operationDialog.type === "confirm" ? operationDialog.title : operationDialog.group ? "重命名分组" : operationDialog.type === "item" ? operationDialog.item ? "编辑启动项" : "添加启动项" : "新建分组"}</h2>
              <button className="operation-close" onClick={() => setOperationDialog(null)} aria-label="关闭">×</button>
            </div>
            {operationDialog.type === "confirm" ? (
              <p className="operation-message">{operationDialog.message}</p>
            ) : (
              <div className="operation-fields">
                <label>
                  <span>{operationDialog.type === "group" ? "分组名称" : "名称"}</span>
                  <input
                    autoFocus
                    value={operationName}
                    onChange={(event) => setOperationName(event.target.value)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter") void handleSaveOperation();
                    }}
                  />
                </label>
                {operationDialog.type === "item" && (
                  <label>
                    <span>启动路径</span>
                    <input
                      value={operationPath}
                      onChange={(event) => setOperationPath(event.target.value)}
                      onKeyDown={(event) => {
                        if (event.key === "Enter") void handleSaveOperation();
                      }}
                    />
                  </label>
                )}
              </div>
            )}
            <footer className="operation-actions">
              <button className="btn" onClick={() => setOperationDialog(null)}>取消</button>
              <button
                className={operationDialog.type === "confirm" ? "btn danger-btn" : "btn primary-btn"}
                onClick={() => void (operationDialog.type === "confirm" ? handleConfirmOperation() : handleSaveOperation())}
              >
                {operationDialog.type === "confirm" ? operationDialog.confirmLabel : "保存"}
              </button>
            </footer>
          </section>
        </div>
      )}
      {contextItem && (
        <div
          className="context-menu"
          style={{ left: contextItem.x, top: contextItem.y }}
          onClick={(event) => event.stopPropagation()}
        >
          <button onClick={() => void handleLaunch(contextItem.item)}>启动</button>
          <button onClick={() => void handleEditItem(contextItem.group, contextItem.item)}>编辑</button>
          {config.groups.length > 1 && (
            <div className="context-move">
              <span>移动到</span>
              {config.groups
                .filter((group) => group.id !== contextItem.group.id)
                .map((group) => (
                  <button key={group.id} onClick={() => void handleMoveItem(contextItem.group, contextItem.item, group.id)}>
                    {group.name}
                  </button>
                ))}
            </div>
          )}
          <button className="danger" onClick={() => void handleDeleteItem(contextItem.group, contextItem.item.id)}>
            移除
          </button>
        </div>
      )}
      {status && <div className="toast">{status}</div>}
    </div>
  );
}
