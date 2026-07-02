#!/usr/bin/env node
/**
 * Minimal MCP stdio server mock for chrome-devtools flows.
 * Implements: initialize, tools/list, tools/call.
 *
 * Tools:
 * - list_pages(): returns a single mock page
 * - select_page({ id }): selects the active page
 * - evaluate_script({ script }): returns url/title/text for the selected page
 * - take_snapshot(): returns a tiny accessibility-tree-like snapshot
 */

import readline from "node:readline";

const pages = [
  { id: "1", title: "Example Domain", url: "https://example.com" },
];

/** @type {{selectedPageId: string}} */
const state = { selectedPageId: "1" };

function reply(id, result) {
  process.stdout.write(JSON.stringify({ jsonrpc: "2.0", id, result }) + "\n");
}

function error(id, message) {
  process.stdout.write(
    JSON.stringify({ jsonrpc: "2.0", id, error: { code: -32000, message } }) + "\n",
  );
}

function toolResultText(text, data) {
  return {
    content: [
      {
        type: "text",
        text,
        ...(data ? { data } : {}),
      },
    ],
  };
}

function listTools() {
  return {
    tools: [
      {
        name: "list_pages",
        description: "List debuggable pages (mock).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
      },
      {
        name: "select_page",
        description: "Select the active page (mock).",
        inputSchema: {
          type: "object",
          properties: { id: { type: "string" } },
          required: ["id"],
          additionalProperties: false,
        },
      },
      {
        name: "evaluate_script",
        description: "Evaluate JS in the page context (mock).",
        inputSchema: {
          type: "object",
          properties: { script: { type: "string" } },
          required: ["script"],
          additionalProperties: false,
        },
      },
      {
        name: "take_snapshot",
        description: "Return an accessibility snapshot (mock).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
      },
    ],
  };
}

function callTool(name, args) {
  if (name === "list_pages") {
    return toolResultText("Listed pages.", { pages });
  }

  if (name === "select_page") {
    const id = args?.id;
    if (!id) return toolResultText("Missing id.");
    state.selectedPageId = id;
    return toolResultText(`Selected page ${id}.`, { selectedPageId: id });
  }

  if (name === "evaluate_script") {
    const page = pages.find((p) => p.id === state.selectedPageId) ?? pages[0];
    const text =
      "Example Domain\n\nThis domain is for use in illustrative examples in documents.\nMore information...";
    return toolResultText("Evaluated script.", {
      title: page.title,
      url: page.url,
      text,
      // Also echo back the script for debuggability.
      script: args?.script ?? "",
    });
  }

  if (name === "take_snapshot") {
    return toolResultText("Took snapshot.", {
      snapshot: {
        role: "document",
        name: "Example Domain",
        children: [{ role: "heading", name: "Example Domain" }],
      },
    });
  }

  return toolResultText(`Unknown tool: ${name}`);
}

const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
rl.on("line", (line) => {
  let msg;
  try {
    msg = JSON.parse(line);
  } catch {
    return;
  }

  const { id, method, params } = msg;
  if (!method) return;

  try {
    if (method === "initialize") {
      reply(id, {
        protocolVersion: "2024-11-05",
        capabilities: { tools: {} },
        serverInfo: { name: "chrome-devtools-mock", version: "1.0.0" },
      });
      return;
    }

    if (method === "notifications/initialized") return;

    if (method === "tools/list") {
      reply(id, listTools());
      return;
    }

    if (method === "tools/call") {
      const toolName = params?.name;
      const argumentsObj = params?.arguments ?? {};
      reply(id, callTool(toolName, argumentsObj));
      return;
    }

    error(id, `Unknown method: ${method}`);
  } catch (e) {
    error(id, e instanceof Error ? e.message : "Unknown error");
  }
});

