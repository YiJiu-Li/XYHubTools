# XY Pipeline distribution

`pipeline.tgz` is a standard UPM local tarball of the maintained Pipeline source in
the development project's `Packages/com.unity.pipeline` directory. It contains
the source, assembly definitions, compiler libraries, documentation, LICENSE.md
and Third Party Notices.md. Original Unity license terms still apply.

XY distributes this snapshot as `0.8.0-xy.1`, based on `0.8.0-exp.1`. Its package
metadata declares Unity 2022.3, matching the existing compatibility modifications
in this development project. This is not a claim of compatibility with all Unity
versions; future version adaptations must be made in the maintained source and
rebundled. Upstream package signatures are excluded because this is a modified
distribution, not an unchanged signed upstream package.

Build from the development project root:

```powershell
python Assets/XYHubTools/MCP~/bundle_pipeline.py --source Packages/com.unity.pipeline
```

The build is deterministic. `bundle.json` records the archive SHA-256 and version.
Keep both generated files in the XY Git repository.

On first editor load, `XY.PipelineBootstrap` checks installed UPM packages. If
Pipeline is missing, it copies this archive to `Packages/XYBridge~/` and installs
it with Unity Package Manager. Dependencies are resolved by UPM (network access
may be needed on first install). The manifest references a project-relative
archive path, so commit the archive, manifest and package lock together.

Existing Pipeline installations are preserved, including previous XY snapshots.
Updating XY alone does not replace an existing Pipeline installation. To opt into
a new bundled snapshot, remove Pipeline via Package Manager and choose
`Tools > XY Hub > 检查或安装 Pipeline`. The same menu retries a failed installation.
The original development project's embedded Pipeline package remains untouched.
