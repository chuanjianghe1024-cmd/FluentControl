import { z } from "zod";

export const MAX_BUNDLE_BYTES = 1024 * 1024;
export class BundleError extends Error {}
export const continuous = new Set(["brightness", "contrast", "gain-red", "gain-green", "gain-blue", "black-red", "black-green", "black-blue", "saturation", "hue", "saturation-red", "saturation-yellow", "saturation-green", "saturation-cyan", "saturation-blue", "saturation-magenta", "speaker", "sharpness"]);
const discrete = new Set(["temperature", "color-preset", "gamma", "input", "monitor-mute", "scaling", "display-mode", "osd-language"]);
const name = z.string().trim().min(1).max(80);
const text = (length: number) => z.string().max(length).default("");
const mapping = z.object({ enabled: z.boolean().default(false), minimum: z.number().min(0).default(0), maximum: z.number().max(100).default(100), offset: z.number().min(-50).max(50).default(0), curve: z.number().min(.2).max(5).default(1) }).strict().refine(v => v.minimum < v.maximum, "亮度映射范围无效");
const monitorSchema = z.object({
  slot: z.string().min(1).max(40), modelId: z.string().regex(/^(?:[A-Z0-9]{7})?$/),
  modelName: text(128), displayName: text(80), brand: text(80),
  values: z.record(z.string(), z.number().min(0).max(65535)).superRefine((values, ctx) => {
    if (Object.keys(values).length > 64) ctx.addIssue({ code: "custom", message: "每台屏幕最多 64 项" });
    for (const [key, value] of Object.entries(values)) {
      if ((!continuous.has(key) && !discrete.has(key)) || (continuous.has(key) && value > 100)) ctx.addIssue({ code: "custom", message: `不可分享的参数：${key}` });
    }
  }), brightness: mapping.nullable().optional()
}).strict();
const sceneSchema = z.object({ name, applications: z.array(name).max(32).default([]), monitors: z.array(monitorSchema).min(1).max(16) }).strict().superRefine((scene, ctx) => {
  if (new Set(scene.monitors.map(m => m.slot)).size !== scene.monitors.length) ctx.addIssue({ code: "custom", message: "场景内的屏幕 slot 重复" });
});
const bundleSchema = z.object({ schema: z.literal("fluentcontrol.monitor-bundle"), version: z.literal(2), name,
  groups: z.array(z.object({ name, profiles: z.array(sceneSchema).min(1).max(512) }).strict()).min(1).max(128)
}).strict().superRefine((bundle, ctx) => {
  const scenes = bundle.groups.flatMap(g => g.profiles);
  if (scenes.length > 512) ctx.addIssue({ code: "custom", message: "配置包最多 512 个场景" });
  const slots = new Map<string, string>();
  for (const monitor of scenes.flatMap(s => s.monitors)) {
    if (slots.has(monitor.slot) && slots.get(monitor.slot) !== monitor.modelId) ctx.addIssue({ code: "custom", message: "同一 slot 不能对应不同型号" });
    slots.set(monitor.slot, monitor.modelId);
  }
});
export type Bundle = z.infer<typeof bundleSchema>;
export type Scene = Bundle["groups"][number]["profiles"][number];
export type Monitor = Scene["monitors"][number];

export function parseBundle(value: unknown): Bundle {
  if (new TextEncoder().encode(JSON.stringify(value)).length > MAX_BUNDLE_BYTES) throw new BundleError("配置包不能超过 1 MiB");
  function inspect(item: unknown, depth = 0) {
    if (depth > 16) throw new BundleError("配置数据嵌套过深");
    if (typeof item !== "object" || item === null) return;
    for (const key of Object.keys(item)) {
      if (["__proto__", "prototype", "constructor"].includes(key)) throw new BundleError("配置包含不支持的字段");
      inspect((item as Record<string, unknown>)[key], depth + 1);
    }
  }
  inspect(value);
  if (typeof value === "object" && value !== null && "schema" in value && value.schema === "fluentcontrol.monitor-profile") {
    const legacy = z.object({ schema: z.literal("fluentcontrol.monitor-profile"), version: z.literal(1), name,
      applications: z.array(name).max(32).default([]), monitors: z.array(monitorSchema).min(1).max(16) }).strict().parse(value);
    if (legacy.monitors.some(m => !m.modelId)) throw new BundleError("旧版 v1 配置必须带有型号");
    value = { schema: "fluentcontrol.monitor-bundle", version: 2, name: legacy.name,
      groups: [{ name: legacy.name, profiles: [{ name: legacy.name, applications: legacy.applications, monitors: legacy.monitors }] }] };
  }
  const result = bundleSchema.parse(value);
  if (new TextEncoder().encode(JSON.stringify(result)).length > MAX_BUNDLE_BYTES) throw new BundleError("转换后的配置包超过 1 MiB");
  return result;
}
export function summarize(bundle: Bundle) {
  const scenes = bundle.groups.flatMap(g => g.profiles);
  const models = [...new Map(scenes.flatMap(s => s.monitors).map(m => [m.modelId || JSON.stringify([m.modelName, m.brand]), { modelId: m.modelId, modelName: m.modelName, brand: m.brand }])).values()];
  return { scenes, models, applications: [...new Set(scenes.flatMap(s => s.applications))] };
}
export const publicationSchema = z.object({ title: name, description: z.string().trim().max(2000).default(""), visibility: z.enum(["public", "draft"]).default("public"), bundle: z.unknown(), expectedRevision: z.number().int().positive().optional() }).strict();
export type Publication = Omit<z.infer<typeof publicationSchema>, "bundle"> & { bundle: Bundle };
export function parsePublication(value: unknown): Publication {
  const direct = typeof value === "object" && value !== null && "schema" in value;
  if (direct) { const bundle = parseBundle(value); return { title: bundle.name, description: "", visibility: "public", bundle }; }
  const input = publicationSchema.parse(value);
  const bundle = parseBundle(input.bundle);
  bundle.name = input.title;
  return { ...input, bundle };
}
export const parameterNames: Record<string, string> = { brightness: "亮度", contrast: "对比度", speaker: "屏幕音量", "color-preset": "色温预设", temperature: "色温", "gain-red": "红色增益", "gain-green": "绿色增益", "gain-blue": "蓝色增益", gamma: "Gamma", input: "输入源", sharpness: "锐度", saturation: "饱和度", hue: "色相", "display-mode": "场景模式", scaling: "画面缩放", "monitor-mute": "静音", "osd-language": "菜单语言" };
