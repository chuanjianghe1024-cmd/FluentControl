import type { Bundle, Publication } from "../src/lib/bundle";
export const fixture = (): Bundle => ({ schema: "fluentcontrol.monitor-bundle", version: 2, name: "办公与游戏 · 中文名称", groups: [{ name: "本地 / 默认分组", profiles: [
  { name: "办公", applications: ["Photoshop"], monitors: [
    { slot: "left", modelId: "HWV1234", modelName: "MateView", displayName: "左屏", brand: "Huawei", values: { brightness: 42, contrast: 65 }, brightness: { enabled: true, minimum: 10, maximum: 90, offset: 5, curve: 1.2 } },
    { slot: "right", modelId: "DEL1234", modelName: "UltraSharp", displayName: "右屏", brand: "Dell", values: { brightness: 50, contrast: 60 }, brightness: null }
  ] },
  { name: "夜间游戏", applications: ["Test Game"], monitors: [{ slot: "right", modelId: "DEL1234", modelName: "UltraSharp", displayName: "右屏", brand: "Dell", values: { brightness: 20 } }] }
] }] });
export const publication = (): Publication => ({ title: "办公与游戏 · 中文名称", description: "不同屏幕也有合适的画面", visibility: "public", bundle: fixture() });
