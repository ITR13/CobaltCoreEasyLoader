using Microsoft.Extensions.Logging;
using Nanoray.PluginManager;
using Nickel;
using OneOf;
using OneOf.Types;

namespace CobaltCoreEasyLoader;

public class EasyLoader(
    IPluginLoader<IModManifest, Mod> pluginLoader,
    Func<IPluginPackage<IModManifest>, ILogger> modLoggerGetter,
    Func<IPluginPackage<IModManifest>, IModHelper> modHelperGetter,
    ExtendableAssemblyPluginLoaderParameterInjector<IModManifest> parameterInjector
)
    : IPluginLoader<IModManifest, Mod>
{
    /*  ModFolder
     *      Sprites/
     *          Character/
     *              CharA
     *              CharB
     *          Cards
     *              CardA
     *              CardB
     *          ShipParts
     *          Artifacts
     *      Localization/
     *      SomeAssembly.dll
     */

    public OneOf<Yes, No, Error<string>> CanLoadPlugin(IPluginPackage<IModManifest> package)
    {
        return package.Manifest.ModType == "EasyLoader" ? pluginLoader.CanLoadPlugin(package) : new No();
    }

    public OneOf<Mod, Error<string>> LoadPlugin(IPluginPackage<IModManifest> package)
    {
        var root = package.PackageRoot;
        var logger = modLoggerGetter(package);
        var helper = modHelperGetter(package);

        var sprites = MaybeLoadSprites(logger, root.GetRelativeDirectory("Sprites"), helper.Content.Sprites);

        var injector = new CompoundAssemblyPluginLoaderParameterInjector<IModManifest>(
            new ValueAssemblyPluginLoaderParameterInjector<IModManifest, Dictionary<string, ISpriteEntry>>(sprites)
        );

        parameterInjector.RegisterParameterInjector(injector);
        try
        {
            return pluginLoader.LoadPlugin(package);
        }
        finally
        {
            parameterInjector.UnregisterParameterInjector(injector);
        }
    }

    private Dictionary<string, ISpriteEntry> MaybeLoadSprites(
        ILogger logger,
        IDirectoryInfo spriteRoot,
        IModSprites modSprites
    )
    {
        if (!spriteRoot.Exists)
        {
            logger.LogWarning("Failed to find Sprites directory");
            return new Dictionary<string, ISpriteEntry>();
        }

        var sprites = new Dictionary<string, ISpriteEntry>();
        foreach (var fileInfo in spriteRoot.GetFilesRecursively())
        {
            var ext = Path.GetExtension(fileInfo.Name);
            if (ext != ".png")
            {
                logger.LogWarning("Found non png file in Sprites directory: {}", fileInfo.FullName);
                continue;
            }

            var fullName = Path
                .GetRelativePath(spriteRoot.FullName, fileInfo.FullName)[..^4]
                .ToLower()
                .Replace(Path.DirectorySeparatorChar, '/');
            var entry = modSprites.RegisterSprite(fullName, fileInfo);
            sprites.Add(fullName, entry);
        }

        logger.LogInformation("Successfully loaded {} sprites", sprites.Count);
        return sprites;
    }
}