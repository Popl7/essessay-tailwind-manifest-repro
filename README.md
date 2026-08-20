# essessay-tailwind-manifest-repro

Minimal repro for [dotnet/aspnetcore#68641](https://github.com/dotnet/aspnetcore/issues/68641) /
[dotnet/sdk#55883](https://github.com/dotnet/sdk/issues/55883).

> **You're on the `proposed-diagnostic-fix` branch.** Adds a build-time
> diagnostic (`WarnAboutUndiscoveredWwwrootFiles`, hooked `AfterTargets="Build"`)
> that compares the final `wwwroot/**` file listing against `@(StaticWebAsset)`'s
> `Identity` metadata (the real physical path each discovered asset points at —
> `RelativePath` is a fingerprint *pattern* like `favicon#[.{fingerprint}]?.ico`,
> not a literal path, so it can't be used for this comparison) and emits an
> `SWA001` warning for any file present on disk with no matching entry — exactly
> this bug's signature.
>
> Validated as both a true positive and a true negative, under a real
> `dotnet publish` (not just `dotnet build`):
> - Silent here (the working `official-pattern-repro` fix) and correctly does
>   *not* flag the legitimate, git-tracked `favicon.ico`.
> - Fires exactly once, on exactly the broken file, when the identical block is
>   applied unchanged to the known-broken `no-external-process-repro` variant
>   — see [`proposed-diagnostic-fix-negative-test`](../../tree/proposed-diagnostic-fix-negative-test).
>
> This is offered as a *today* workaround (drop it into any project using this
> pattern to get a build warning instead of a silent production 404) and as a
> concrete starting point for what a real fix inside the SDK could check for.
> See [`resolve-target-direct-hook`](../../tree/resolve-target-direct-hook) for
> a related finding: hooking `BeforeTargets="ResolveProjectStaticWebAssets"`
> directly (a guaranteed ordering edge, rather than being an unordered sibling
> of it via `BeforeTargets="AssignTargetPaths"`) does **not** fix the bug
> without a `Content` item either — ordering was never the deciding factor,
> only explicit `Content` registration is.

> **The `official-pattern-repro` branch** (this one, minus the diagnostic) has
> `GenerateSiteCss` following
> Microsoft's own documented pattern for hooking asset generation into static
> web assets, from
> [Build client web assets for your Razor Class Library](https://devblogs.microsoft.com/dotnet/build-client-web-assets-for-your-razor-class-library/):
> `BeforeTargets="AssignTargetPaths"` (not `BeforeBuild`), the file written to
> `$(IntermediateOutputPath)` rather than directly into `wwwroot`, and linked
> in via an explicit `<Content Include=".." Link="wwwroot/.." />` item instead
> of relying on the `wwwroot/**` wildcard glob to notice a file that appeared
> on disk after project evaluation.
>
> **Finding: this is the first variant that held up** — `site.css` correctly
> gets a route in the manifest and `GET /css/site.css` serves it, 4/4 local
> builds, confirmed on GitHub Actions too (see
> [`.github/workflows/check-official-pattern.yml`](.github/workflows/check-official-pattern.yml)).
> Every other branch here (`main`, `no-external-process-repro`, `dotnet9-repro`,
> `dotnet11-repro`) writes the file directly into `wwwroot` and relies on the
> wildcard glob — that's the part that actually seems to race. The hook name
> alone (`BeforeBuild` vs `AssignTargetPaths`) wasn't the fix; explicitly
> declaring the output as a `Content` item was.

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
