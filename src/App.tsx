import { useState } from "react";

interface Item {
  id: string;
  name: string;
}

interface Group {
  id: string;
  name: string;
  items: Item[];
}

const initialGroups: Group[] = [
  {
    id: "g1",
    name: "常用",
    items: [
      { id: "i1", name: "记事本" },
      { id: "i2", name: "终端" },
    ],
  },
  {
    id: "g2",
    name: "开发",
    items: [{ id: "i3", name: "VS Code" }],
  },
];

export default function App() {
  const [groups] = useState<Group[]>(initialGroups);
  const [active, setActive] = useState("g1");
  const [query, setQuery] = useState("");

  const current = groups.find((g) => g.id === active) ?? groups[0];
  const visible = current.items.filter((it) =>
    it.name.toLowerCase().includes(query.toLowerCase()),
  );

  return (
    <div className="app">
      <div className="search-bar">
        <input
          className="search-input"
          placeholder="输入以搜索应用… (Alt+Space)"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>
      <div className="body">
        <aside className="groups">
          {groups.map((g) => (
            <button
              key={g.id}
              className={"group-tab" + (g.id === active ? " active" : "")}
              onClick={() => setActive(g.id)}
            >
              {g.name}
            </button>
          ))}
        </aside>
        <main className="grid">
          {visible.map((it) => (
            <div key={it.id} className="tile">
              <div className="tile-icon" />
              <span className="tile-name">{it.name}</span>
            </div>
          ))}
        </main>
      </div>
    </div>
  );
}
