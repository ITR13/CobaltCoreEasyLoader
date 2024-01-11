using System.Reflection;
using CobaltCoreEasyLoader.Data;
using Microsoft.Extensions.Logging;
using Nanoray.PluginManager;
using Newtonsoft.Json;
using Nickel;
using OneOf;
using OneOf.Types;
using Tomlet;

namespace CobaltCoreEasyLoader;

using RegisterCardsFunc = Func<IDeckEntry, List<Type>, List<ICardEntry>>;

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
     *      Data/
     *          Decks
     *          Localization
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
        var content = helper.Content;

        var sprites = MaybeLoadSprites(logger, root.GetRelativeDirectory("Sprites"), content.Sprites);
        var decks = MaybeLoadDeck(logger, root.GetRelativeDirectory("Data/Decks"), content.Decks, sprites);

        List<ICardEntry> RegisterCards(IDeckEntry deck, List<Type> cards)
        {
            var registeredCards = new List<ICardEntry>();
            foreach (var type in cards)
            {
                var cardMeta = type.GetCustomAttribute<CardMeta>() ??
                               new CardMeta
                               {
                                   rarity = Rarity.common,
                                   extraGlossary = [],
                                   unreleased = false,
                                   upgradesTo = [Upgrade.A, Upgrade.B],
                                   dontLoc = false,
                                   dontOffer = false,
                                   weirdCard = false,
                               };
                cardMeta.deck = deck.Deck;
                sprites.TryGetValue($"Cards/{type.FullName}", out var art);

                // TODO: Do localization
                var card = content.Cards.RegisterCard(
                    type.FullName ?? type.Name,
                    new CardConfiguration
                    {
                        CardType = type,
                        Meta = cardMeta,
                        Art = art?.Sprite,
                    }
                );
                registeredCards.Add(card);
            }

            return registeredCards;
        }


        var injector = new CompoundAssemblyPluginLoaderParameterInjector<IModManifest>(
            new ValueAssemblyPluginLoaderParameterInjector<IModManifest, Dictionary<string, ISpriteEntry>>(sprites),
            new ValueAssemblyPluginLoaderParameterInjector<IModManifest, Dictionary<string, IDeckEntry>>(decks),
            new ValueAssemblyPluginLoaderParameterInjector<IModManifest, RegisterCardsFunc>(RegisterCards)
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

    private Dictionary<string, IDeckEntry> MaybeLoadDeck(
        ILogger logger,
        IDirectoryInfo deckRoot,
        IModDecks contentDecks,
        Dictionary<string, ISpriteEntry> spriteEntries
    )
    {
        var decks = new Dictionary<string, IDeckEntry>();

        foreach (var fileInfo in deckRoot.Files)
        {
            var ext = Path.GetExtension(fileInfo.Name);
            if (ext != ".json" && ext != ".toml")
            {
                logger.LogWarning("Found non json file in Sprites directory: {}", fileInfo.FullName);
                continue;
            }

            var name = fileInfo.Name[..^5].ToLower();
            var easyDeckDef = ext == ".toml" ? TomlParse<EasyDeckDef>(fileInfo) : JsonParse<EasyDeckDef>(fileInfo);

            var borderSprite = TryGetSprite("bordersprite", true) ?? Spr.cardShared_border_colorless;
            var defaultCardArt = TryGetSprite($"defaultcardart", true) ?? Spr.cards_colorless;
            var overBorderSprite = TryGetSprite("overbordersprite");

            // TODO: Add Localization
            var deck = contentDecks.RegisterDeck(
                name,
                new DeckConfiguration
                {
                    Definition = easyDeckDef,
                    BorderSprite = borderSprite,
                    DefaultCardArt = defaultCardArt,
                    Name = null,
                    OverBordersSprite = overBorderSprite,
                }
            );
            decks.Add(name, deck);

            continue;

            Spr? TryGetSprite(string spriteName, bool notNull = false)
            {
                var path = $"decks/{name}/{spriteName}";
                if (spriteEntries.TryGetValue(path, out var spr)) return spr!.Sprite;
                if (notNull)
                {
                    logger.LogWarning($"Failed to find sprite {path}");
                }

                return null;
            }
        }

        logger.LogInformation("Loaded {} decks", decks.Count);
        return decks;
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

    private T TomlParse<T>(IFileInfo info)
    {
        using var reader = new StreamReader(info.OpenRead());
        var text = reader.ReadToEnd();
        return TomletMain.To<T>(text)!;
    }

    private T JsonParse<T>(IFileInfo info)
    {
        using var reader = new StreamReader(info.OpenRead());
        var text = reader.ReadToEnd();
        return JsonConvert.DeserializeObject<T>(text)!;
    }
}