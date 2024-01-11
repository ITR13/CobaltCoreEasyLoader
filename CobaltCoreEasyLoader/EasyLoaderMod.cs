using Microsoft.Extensions.Logging;
using Nanoray.PluginManager;
using Nickel;

namespace CobaltCoreEasyLoader;

public class EasyLoaderMod : Mod
{
    public EasyLoaderMod(
        ExtendablePluginLoader<IModManifest, Mod> extendablePluginLoader,
        Func<IPluginPackage<IModManifest>, ILogger> modLoggerGetter,
        Func<IPluginPackage<IModManifest>, IModHelper> modHelperGetter,
        
        IAssemblyPluginLoaderLoadContextProvider<IAssemblyModManifest> loadContextProvider,
        ExtendableAssemblyPluginLoaderParameterInjector<IModManifest> parameterInjector,
        IAssemblyEditor? assemblyEditor
    )
    {
        extendablePluginLoader.RegisterPluginLoader(
            new EasyLoader(
                new SpecializedConvertingManifestPluginLoader<IAssemblyModManifest, IModManifest, Mod>(
                    new AssemblyPluginLoader<IAssemblyModManifest, Mod, Mod>(
                        requiredPluginDataProvider: p => new AssemblyPluginLoaderRequiredPluginData
                        {
                            UniqueName = p.Manifest.UniqueName,
                            EntryPointAssembly = p.Manifest.EntryPointAssembly,
                            EntryPointType = p.Manifest.EntryPointType
                        },
                        loadContextProvider: loadContextProvider,
                        partAssembler: new SingleAssemblyPluginPartAssembler<IAssemblyModManifest, Mod>(),
                        parameterInjector: parameterInjector,
                        assemblyEditor: assemblyEditor
                    ),
                    m => m.AsAssemblyModManifest()
                ),
                modLoggerGetter,
                modHelperGetter,
                parameterInjector
            )
        );
    }
}