#!/usr/bin/env node
/**
 * Minimal MCP stdio server mock for Microsoft 365 mail flows.
 * Implements: initialize, tools/list, tools/call.
 *
 * Tools:
 * - list_inbox(): returns a small, in-memory inbox
 * - add_category({ messageId, category }): mutates categories on a message
 */

import readline from "node:readline";

/** @type {{selected?: string, messages: Array<{id:string, from:string, subject:string, receivedUtc:string, preview:string, categories:string[]}>}} */
const state = {
  messages: [
    {
      id: "m1",
      from: "alice@example.com",
      subject: "Quarterly invoice",
      receivedUtc: new Date(Date.now() - 60_000).toISOString(),
      preview: "Hi — attached is the quarterly invoice. Please confirm receipt.",
      categories: [],
    },
    {
      id: "m2",
      from: "newsletter@example.com",
      subject: "Weekly roundup",
      receivedUtc: new Date(Date.now() - 120_000).toISOString(),
      preview: "This week: new releases, tips, and upcoming events…",
      categories: ["newsletter"],
    },
  ],
};

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
        name: "list_inbox",
        description: "List a small page of inbox messages (mock).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
      },
      {
        name: "add_category",
        description: "Apply a category/label to a message (mock).",
        inputSchema: {
          type: "object",
          properties: {
            messageId: { type: "string" },
            category: { type: "string" },
          },
          required: ["messageId", "category"],
          additionalProperties: false,
        },
      },
    ],
  };
}

function callTool(name, args) {
  if (name === "list_inbox") {
    return toolResultText("Listed inbox messages.", { messages: state.messages });
  }

  if (name === "add_category") {
    const { messageId, category } = args ?? {};
    if (!messageId || !category) return toolResultText("Missing messageId/category.");
    const msg = state.messages.find((m) => m.id === messageId);
    if (!msg) return toolResultText("Message not found.");
    if (!msg.categories.includes(category)) msg.categories.push(category);
    return toolResultText(`Added category '${category}' to ${messageId}.`, {
      messageId,
      categories: msg.categories,
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
        serverInfo: { name: "ms365-mock", version: "1.0.0" },
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

