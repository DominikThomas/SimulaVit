# Project working preferences

- Leave testing and simulation experiments to the user unless they explicitly request them for the current work.
- Do not launch Unity tests, gameplay simulations, benchmarks, or other validation runs without that explicit request. Static source review is permitted.
- Preserve saved user settings. Do not reset configured values to source defaults. The user confirmed Hydrogen → Fermentation mutation probability of **0.02** for the fermentation milestone; preserve it unless they change it or instruct otherwise.

# Temporary validation workspaces

- Prefer reusing existing disposable validation workspaces rather than creating a new complete Unity project copy for every task.
- Place temporary validation projects and artifacts under /work/ or another explicitly ignored temporary directory.
- Never modify or delete a temporary workspace belonging to another active task.
- Clearly identify newly created disposable workspaces in the final report.
- Do not retain large duplicated Unity Library, Temp, or package caches unnecessarily.
- Before deleting a workspace, verify that it contains no unique source changes, valuable diagnostics, or user data.
- Never automatically delete existing workspaces without explicit permission.