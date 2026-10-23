import readline from "node:readline";
import vm from "node:vm";

const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
const messages = input[Symbol.asyncIterator]();
const pending = new Map();
let nextCallId = 0;

function send(message) {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

function readMessage() {
  return messages.next().then(({ value, done }) => {
    if (done) throw new Error("Host closed the sandbox protocol.");
    return JSON.parse(value);
  });
}

async function start() {
  const request = await readMessage();
  if (request.type !== "start" || typeof request.code !== "string" || !Array.isArray(request.tools)) {
    throw new Error("Invalid sandbox start message.");
  }

  input.on("line", (line) => {
    let response;
    try { response = JSON.parse(line); } catch { return; }
    if (response.type !== "tool_result" || typeof response.id !== "string") return;
    const completion = pending.get(response.id);
    if (!completion) return;
    pending.delete(response.id);
    if (response.error) completion.reject(new Error("Host tool call failed."));
    else completion.resolve(response.output);
  });

  const tools = Object.create(null);
  for (const name of request.tools) {
    if (typeof name !== "string" || Object.hasOwn(tools, name)) throw new Error("Invalid tool list.");
    tools[name] = (argumentsValue) => new Promise((resolve, reject) => {
      const id = String(++nextCallId);
      pending.set(id, { resolve, reject });
      send({ type: "tool_call", id, name, arguments: argumentsValue ?? {} });
    });
  }

  const safeConsole = Object.freeze({
    log: (...values) => process.stderr.write(`${values.map(String).join(" ")}\n`),
    error: (...values) => process.stderr.write(`${values.map(String).join(" ")}\n`),
  });
  const sandbox = vm.createContext({ tools, console: safeConsole, JSON, Promise, Math, Date });
  const entryPoint = new vm.Script(`(${request.code})`, { filename: "mosaic-generated.js" }).runInContext(sandbox, { timeout: 100 });
  if (typeof entryPoint !== "function") throw new Error("Generated code must be an async function expression.");
  const output = await entryPoint(tools);
  const serializedOutput = JSON.stringify(output ?? null);
  if (serializedOutput === undefined) throw new Error("Generated code returned a non-JSON value.");
  send({ type: "result", output: JSON.parse(serializedOutput) });
}

start().catch(() => {
  send({ type: "error", error: "Sandbox execution failed." });
  process.exitCode = 1;
});
