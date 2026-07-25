import { defineConfig } from "tsup";

export default defineConfig({
  entry: {
    index: "src/index.ts",
  },
  format: ["esm"],
  platform: "browser",
  target: "es2022",
  dts: false,
  minify: true,
  sourcemap: false,
  splitting: false,
  clean: true,
  noExternal: ["mqtt", "buffer"],
  outDir: "../../TestApp/wwwroot/vendor/mqttrpc-client",
});
