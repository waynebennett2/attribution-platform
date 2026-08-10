export default [
  {
    ignores: ["node_modules/**", "dist/**", "playwright-report/**", "test-results/**"],
  },
  {
    files: ["src/**/*.js", "tests/**/*.js"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
      globals: {
        window: "readonly",
        document: "readonly",
        MutationObserver: "readonly",
        fetch: "readonly",
        console: "readonly",
        setTimeout: "readonly",
        setInterval: "readonly",
        clearInterval: "readonly",
        URLSearchParams: "readonly",
        NodeFilter: "readonly",
        Node: "readonly",
      },
    },
    rules: {
      "no-unused-vars": "warn",
      "no-undef": "error",
      eqeqeq: ["error", "always"],
    },
  },
];
