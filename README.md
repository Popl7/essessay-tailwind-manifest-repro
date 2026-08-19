# essessay-tailwind-manifest-repro

Minimal repro for [dotnet/aspnetcore#68641](https://github.com/dotnet/aspnetcore/issues/68641).

> **You're on the `tutorial-pattern-repro` branch.** Mirrors the exact shape
> taught by real "add Tailwind to ASP.NET Core" tutorials — see
> [this issue comment](https://github.com/dotnet/aspnetcore/issues/68641#issuecomment-5342134333)
> for the specific posts, one of which is written for .NET 10 specifically:
>
> ```xml
> <Target Name="Tailwind" BeforeTargets="Build">
>     <Exec Command="npx @tailwindcss/cli -i ./css/site.css -o ../wwwroot/css/site.css" />
> </Target>
> ```
>
> `BeforeTargets="Build"` (not `BeforeBuild`, not `AssignTargetPaths`), no
> `Inputs`/`Outputs`, no `Content`/`Link` item — just a task that writes
> straight into `wwwroot`, exactly as taught. **Finding: broken, 3/3 local
> builds and confirmed on GitHub Actions** — same bug, same signature, just
> using the literal hook name these tutorials actually teach instead of
> `BeforeBuild`. See
> [`.github/workflows/check-tutorial-pattern.yml`](.github/workflows/check-tutorial-pattern.yml).

## The bug

A bare ASP.NET Core MVC app (.NET 10) with one page. `Essessay/Essessay.csproj`
has a custom MSBuild target that runs the Tailwind CSS CLI during the build and
writes `Essessay/wwwroot/css/site.css`:

```xml
<Target Name="BuildTailwindCss" BeforeTargets="BeforeBuild" Inputs="@(TailwindInput)" Outputs="$(TailwindStamp);$(TailwindCss)">
    <Exec Command="&quot;$(TailwindExe)&quot; --input Styles/app.css --output wwwroot/css/site.css $(TailwindMinify)" WorkingDirectory="$(MSBuildProjectDirectory)" />
    <Touch Files="$(TailwindStamp)" AlwaysCreate="true" />
</Target>
```

That file is served via `app.MapStaticAssets()` in `Program.cs`, which serves
assets from a build-time-generated manifest (`Essessay.staticwebassets.endpoints.json`)
rather than from disk directly. `Essessay/Dockerfile` builds the app with a
single, ordinary `RUN dotnet publish Essessay/Essessay.csproj -c Release -o /app --no-restore`.

**Expected:** `wwwroot/css/site.css` gets a route in the manifest, same as any
other static asset, and `GET /css/site.css` serves it — the home page renders
as a white, green-bordered card.

**Actual:** the route is often missing from the manifest even though the file
is written to disk correctly, every time, at the correct size. `GET /css/site.css`
404s, and the home page renders completely unstyled (plain serif text, no card,
no border) even though its `<link>` tag points at a real, versioned URL.

This app is small enough that **the bug now reproduces with a plain local
`docker build --no-cache`** on every machine we've tried it on — no cloud host
required. That wasn't true of the original (much larger) app this was stripped
down from: there, it was consistently correct on local Docker Desktop builds
and consistently broken on Render.com and GitHub Actions `ubuntu-latest`
builds. Shrinking the app changed the outcome locally, which suggests this is
a genuine race between `BuildTailwindCss`'s `Exec` and the SDK's own static
web assets discovery — the less other work the build has to do, the more
often discovery finishes first. See the issue for the full writeup, including
three other fixes that were tried and didn't hold (`AssignTargetPaths;Publish`
hook, splitting `dotnet build`/`dotnet publish`).

## Reproducing it

Locally:

```bash
docker build --no-cache -t essessay-repro -f Essessay/Dockerfile .
docker run --rm --entrypoint sh essessay-repro -c \
  "grep -o '\"Route\":\"[^\"]*site.css[^\"]*\"' /app/Essessay.staticwebassets.endpoints.json"
```

A correct result prints a `"Route":"css/site.css"` line. The bug's signature is
that line being absent (the command above prints nothing, or grep exits
non-zero). You can also run the app and check directly:

```bash
docker run --rm -p 8080:8080 essessay-repro &
curl -o /dev/null -w '%{http_code}\n' http://localhost:8080/css/site.css   # 404 when the bug hits
```

Or via GitHub Actions:
[`.github/workflows/check-tailwind-manifest.yml`](.github/workflows/check-tailwind-manifest.yml)
builds the Dockerfile with `--no-cache` on `ubuntu-latest` and fails the job if
the route is missing. Trigger it from the Actions tab (`workflow_dispatch`), or
fork the repo — it has no external dependencies beyond what the Dockerfile
itself downloads.

Past runs that reproduced it:
- https://github.com/Popl7/essessay-tailwind-manifest-repro/actions/runs/32241988885
- https://github.com/Popl7/essessay-tailwind-manifest-repro/actions/runs/32241993765

## The working fix

Generate the CSS as its own, separate `dotnet msbuild -t:BuildTailwindCss`
invocation *before* `dotnet publish` starts, rather than letting `dotnet publish`
run that target itself — see the real app's
[Dockerfile](https://gitlab.com/StevenT/essessay/-/blob/develop/Essessay/Dockerfile),
which has held up across multiple real deploys.
