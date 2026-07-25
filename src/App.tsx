import { useCallback, useEffect, useState } from "react";
import { listen, TauriEvent } from "@tauri-apps/api/event";
import { open } from "@tauri-apps/plugin-dialog";
import { open as shellOpen } from "@tauri-apps/plugin-shell";
import * as api from "./api";
import type { AppConfig, Group, Item, SearchResult } from "./types";

const DEFAULT_CONFIG: AppConfig = {
  groups: [],
  settings: { hotkey: "Alt+Space", theme: "dark", opacity: 1 },
};

export default function App() {
  const [config, setConfig] = useState<AppConfig>(DEFAULT_CONFIG);
  const [activeId, setActiveId] = useState("");
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResult[]>([]);
  const [dragging, setDragging] = useState(false);

  const applyTheme = useCallback((theme: string, opacity: number) => {
    const root = document.documentElement;
    root.setAttribute("data-theme", theme);
    root.style.setProperty("--opacity", String(opacity));
  }, []);

  const refresh = useCallback(async () => {
    const cfg = await api.loadConfig();
    setConfig(cfg);
    if (!activeId && cfg.groups.length) setActiveId(cfg.groups[0].id);
    applyTheme(cfg.settings.theme, cfg.settings.opacity);
  }, [activeId, applyTheme]);

  useEffect(() => {
    refresh();
  }, [refresh]);

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

  // 从资源管理器拖拽文件/快捷方式到窗口 -> 加入当前分组
  useEffect(() => {
    const unsubs: Array<() => void> = [];
    const onEnter = () => setDragging(true);
    const onLeave = () => setDragging(false);
    listen(TauriEvent.DRAG_ENTER, onEnter).then((u) => unsubs.push(u));
    listen(TauriEvent.DRAG_OVER, onEnter).then((u) => unsubs.push(u));
    listen(TauriEvent.DRAG_LEAVE, onLeave).then((u) => unsubs.push(u));
    listen<{ paths?: string[] }>(TauriEvent.DRAG_DROP, (e) => {
      setDragging(false);
      const paths = e.payload.paths;
      if (!paths || paths.length === 0) return;
      const targetGroup = activeId || config.groups[0]?.id;
      if (!targetGroup) return;
      (async () => {
        let cfg: AppConfig | null = null;
        for (const p of paths) {
          const name = p.split(/[\\/]/).pop() ?? p;
          cfg = await api.addItem(targetGroup, name, p);
        }
        if (cfg) setConfig(cfg);
      })();
    }).then((u) => unsubs.push(u));
    return () => {
      unsubs.forEach((u) => u());
    };
  }, [activeId, config]);

  const handleAddGroup = async () => {
    const name = window.prompt("分组名称", "新分组");
    if (!name) return;
    const cfg = await api.addGroup(name);
    setConfig(cfg);
    setActiveId(cfg.groups[cfg.groups.length - 1].id);
  };

  const handleDeleteGroup = async (id: string) => {
    if (!window.confirm("删除该分组及其所有图标？")) return;
    const cfg = await api.removeGroup(id);
    setConfig(cfg);
    if (activeId === id) setActiveId(cfg.groups[0]?.id ?? "");
  };

  const handleRename = async (g: Group) => {
    const name = window.prompt("重命名分组", g.name);
    if (!name) return;
    const cfg = await api.renameGroup(g.id, name);
    setConfig(cfg);
  };

  const handleAddItem = async (g: Group) => {
    const picked = await open({
      multiple: true,
      filters: [
        { name: "快捷方式/程序", extensions: ["exe", "lnk", "url", "bat", "cmd"] },
      ],
    });
    if (!picked) return;
    const files = Array.isArray(picked) ? picked : [picked];
    let cfg = config;
    for (const f of files) {
      const name = f.split(/[\\/]/).pop() ?? f;
      cfg = await api.addItem(g.id, name, f);
    }
    setConfig(cfg);
  };

  const handleLaunch = async (it: Item) => {
    await shellOpen(it.path);
  };

  const handleDeleteItem = async (g: Group, itemId: string) => {
    const cfg = await api.removeItem(g.id, itemId);
    setConfig(cfg);
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

  const searching = query.trim().length > 0;
  const activeGroup = config.groups.find((g) => g.id === activeId);

  return (
    <div className={"app" + (dragging ? " dragging" : "")}>
      <header className="toolbar">
        <input
          className="search-input"
          placeholder="搜索应用…（Alt+F1 呼出）"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        <button className="btn" onClick={handleAddGroup}>
          ＋ 分组
        </button>
        <button className="btn" onClick={handleThemeToggle} title="切换明暗主题">
          {config.settings.theme === "dark" ? "浅色" : "深色"}
        </button>
        <label className="opacity">
          透明度
          <input
            type="range"
            min={0.4}
            max={1}
            step={0.05}
            value={config.settings.opacity}
            onChange={handleOpacity}
          />
        </label>
      </header>

      {searching ? (
        <main className="grid">
          {results.length === 0 && <div className="empty">无匹配结果</div>}
          {results.map((r) => (
            <div
              key={r.item.id}
              className="tile"
              onDoubleClick={() => handleLaunch(r.item)}
              title={`${r.item.path}（双击启动）`}
            >
              <div className="tile-icon" />
              <span className="tile-name">{r.item.name}</span>
              <span className="tile-sub">{r.group_name}</span>
            </div>
          ))}
        </main>
      ) : (
        <div className="body">
          <aside className="groups">
            {config.groups.map((g) => (
              <div
                key={g.id}
                className={"group-row" + (g.id === activeId ? " active" : "")}
              >
                <button
                  className="group-tab"
                  onClick={() => setActiveId(g.id)}
                  onDoubleClick={() => handleRename(g)}
                >
                  {g.name}
                </button>
                <span className="group-actions">
                  <button title="添加图标" onClick={() => handleAddItem(g)}>
                    ＋
                  </button>
                  <button title="重命名" onClick={() => handleRename(g)}>
                    R
                  </button>
                  <button title="删除分组" onClick={() => handleDeleteGroup(g.id)}>
                    ×
                  </button>
                </span>
              </div>
            ))}
            {config.groups.length === 0 && (
              <div className="empty">暂无分组，点击「＋ 分组」</div>
            )}
          </aside>
          <main className="grid">
            {activeGroup?.items.map((it) => (
              <div
                key={it.id}
                className="tile"
                onDoubleClick={() => handleLaunch(it)}
                title={`${it.path}（双击启动）`}
              >
                <button
                  className="tile-del"
                  title="移除"
                  onClick={() => handleDeleteItem(activeGroup, it.id)}
                >
                  ×
                </button>
                <div className="tile-icon" />
                <span className="tile-name">{it.name}</span>
              </div>
            ))}
            {activeGroup && activeGroup.items.length === 0 && (
              <div className="empty">空分组，点击左侧「＋」添加图标</div>
            )}
          </main>
        </div>
      )}
    </div>
  );
}
