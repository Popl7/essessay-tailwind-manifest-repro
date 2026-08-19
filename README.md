# essessay-tailwind-manifest-repro

Minimal repro for [dotnet/aspnetcore#68641](https://github.com/dotnet/aspnetcore/issues/68641).

## The bug

This is a full copy of the [Essessay](https://gitlab.com/StevenT/essessay) ASP.NET
Core app (.NET 10), kept deliberately at the point *before* a fix was applied, so
the bug is still live here.

`Essessay/Essessay.csproj` has a custom MSBuild target that runs the Tailwind CSS
CLI during the build and writes `Essessay/wwwroot/css/site.css`:

```xml
<Target Name="BuildTailwindCss" BeforeTargets="BeforeBuild" Inputs="@(TailwindInput)" Outputs="$(TailwindStamp);$(TailwindCss)">
    <Exec Command="&quot;$(TailwindExe)&quot; --input Styles/app.css --output wwwroot/css/site.css $(TailwindMinify)" WorkingDirectory="$(MSBuildProjectDirectory)" />
    <Touch Files="$(TailwindStamp)" AlwaysCreate="true" />
</Target>
```

That file is served via `app.MapStaticAssets()` in `Program.cs`, which serves
assets from a build-time-generated manifest (`Essessay.staticwebassets.endpoints.json`)
rather than from disk directly.

`Essessay/Dockerfile` builds the app with a single, ordinary
`RUN dotnet publish Essessay/Essessay.csproj -c Release -o /app --no-restore`.

**Expected:** `wwwroot/css/site.css` gets a route in the manifest, same as any
other static asset, and `GET /css/site.css` serves it.

**Actual, depending on where the `docker build` runs:**

| Where | Result |
|---|---|
| Local Docker Desktop (macOS, arm64 native, amd64 via QEMU, and under `--memory`/`--cpu-quota` limits down to ~768 MB / 1 CPU) | Always correct |
| Render.com (documented free-tier build machine: 2 CPU / 8 GB RAM) | Always missing |
| GitHub Actions `ubuntu-latest` (4 CPU / 15 GB RAM, plain Docker, no sandboxing) | Always missing |

The file itself is written correctly every time, at the correct size. Only its
entry in the manifest goes missing — and only on some build hosts, never on
others, regardless of available CPU/memory. See the issue for the full writeup,
including three other fixes that were tried and didn't hold (`AssignTargetPaths;Publish`
hook, splitting `dotnet build`/`dotnet publish`).

## Reproducing it

[`.github/workflows/check-tailwind-manifest.yml`](.github/workflows/check-tailwind-manifest.yml)
builds the Dockerfile with `--no-cache` and fails the job if `css/site.css` has
no route in the published manifest. Trigger it yourself via the Actions tab
(`workflow_dispatch`), or fork the repo — it's a complete, working app with no
external dependencies beyond what the Dockerfile downloads.

Past runs that reproduced it:
- https://github.com/Popl7/essessay-tailwind-manifest-repro/actions/runs/32241988885
- https://github.com/Popl7/essessay-tailwind-manifest-repro/actions/runs/32241993765

To check locally:

```bash
docker build --no-cache -t essessay-repro -f Essessay/Dockerfile .
docker run --rm --entrypoint sh essessay-repro -c \
  "grep -o '\"Route\":\"[^\"]*site.css[^\"]*\"' /app/Essessay.staticwebassets.endpoints.json"
```

A correct result includes a `"Route":"css/site.css"` line; the bug's signature
is that line being absent while the `Identity/css/site.css` routes (from
Identity UI's Razor Class Library) still show up.

## The working fix

Essessay's own repo (not this one) now generates the CSS as its own, separate
`dotnet msbuild -t:BuildTailwindCss` invocation *before* `dotnet publish` starts,
rather than letting `dotnet publish` run that target itself — see
[Essessay/Dockerfile](https://gitlab.com/StevenT/essessay/-/blob/develop/Essessay/Dockerfile)
on the main repo. That's the only approach that has held up across multiple
real deploys.
