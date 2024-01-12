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
     *          Characters/
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
        var animations = MaybeLoadAnimations(
            logger,
            root.GetRelativeDirectory("Sprites/Characters"),
            content.Characters,
            sprites,
            decks
        );

        Deck GetDeck(string deckName)
        {
            if (decks.TryGetValue(deckName, out var deck))
            {
                return deck.Deck;
            }

            logger.LogWarning("Failed to find deck {}", deckName);
            return Deck.colorless;
        }

        List<ICardEntry> RegisterCards(string deckName, List<Type> cards)
        {
            var registeredCards = new List<ICardEntry>();
            var deck = GetDeck(deckName);
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
                cardMeta.deck = deck;
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

        ICharacterEntry RegisterCharacter(
            string deckName,
            List<Type> starterCard,
            List<Type> starterArtifacts,
            bool startLocked
        )
        {
            var deck = GetDeck(deckName);

            // TODO: Do localization
            var character = content.Characters.RegisterCharacter(
                deckName,
                new CharacterConfiguration
                {
                    Deck = deck,
                    StarterCardTypes = starterCard,
                    StarterArtifactTypes = starterArtifacts,
                    StartLocked = startLocked,

                    BorderSprite = TryGetSprite("panel") ?? Spr.panels_char_colorless,
                }
            );

            if (!animations.TryGetValue(deck, out var chAnimations))
            {
                logger.LogError("Couldn't find {} in Sprites/Characters", deckName);
            }
            else
            {
                if (!chAnimations.ContainsKey("neutral"))
                {
                    logger.LogError("Couldn't find Sprites/Characters/{}/neutral", deckName);
                }

                if (!chAnimations.ContainsKey("mini"))
                {
                    logger.LogError("Couldn't find Sprites/Characters/{}/mini", deckName);
                }
            }

            return character;

            Spr? TryGetSprite(string spriteName)
            {
                var path = $"characters/{deckName}/{spriteName}";
                if (sprites.TryGetValue(path, out var spr)) return spr.Sprite;
                logger.LogWarning($"Failed to find sprite {path}");

                return null;
            }
        }

        ICharacterEntry RegisterCharacter2(
            string deckName,
            List<Type> starterCard,
            List<Type> starterArtifacts
        )
        {
            return RegisterCharacter(deckName, starterCard, starterArtifacts, false);
        }

        var injector = new CompoundAssemblyPluginLoaderParameterInjector<IModManifest>(
            InjectParam(sprites),
            InjectParam(decks),
            InjectParam(animations),
            InjectParam(RegisterCards),
            InjectParam(RegisterCharacter),
            InjectParam(RegisterCharacter2)
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

    private static ValueAssemblyPluginLoaderParameterInjector<IModManifest, T> InjectParam<T>(T value)
    {
        return new ValueAssemblyPluginLoaderParameterInjector<IModManifest, T>(value);
    }

    private Dictionary<Deck, Dictionary<string, ICharacterAnimationEntry>> MaybeLoadAnimations(
        ILogger logger,
        IDirectoryInfo charactersDirectory,
        IModCharacters modCharacters,
        Dictionary<string, ISpriteEntry> sprites,
        Dictionary<string, IDeckEntry> decks
    )
    {
        if (!charactersDirectory.Exists)
        {
            logger.LogWarning("Failed to find Sprites/Characters directory");
            return new();
        }

        var animations = new Dictionary<Deck, Dictionary<string, ICharacterAnimationEntry>>();
        foreach (var chDirectory in charactersDirectory.Directories)
        {
            var chName = chDirectory.Name.ToLower();
            if (!decks.TryGetValue(chName, out var deckEntry))
            {
                logger.LogWarning("Failed to find deck {}", chName);
                continue;
            }

            var deck = deckEntry.Deck;
            var chAnimations = new Dictionary<string, ICharacterAnimationEntry>();
            animations.Add(deck, chAnimations);

            foreach (var anDirectory in chDirectory.Directories)
            {
                var anName = anDirectory.Name.ToLower();
                var files = anDirectory.Files
                    .Select(file => file.Name)
                    .Where(name => Path.GetExtension(name) == ".png")
                    .Select(name => name[..^4].ToLower())
                    .OrderBy(SafeParse)
                    .ToList();
                files.Sort();

                var frames = new List<Spr>();
                foreach (var file in files)
                {
                    var path = $"characters/{chName}/{anName}/{file}";
                    frames.Add(sprites[path].Sprite);
                }

                var animation = modCharacters.RegisterCharacterAnimation(
                    $"{chName}/{anName}",
                    new CharacterAnimationConfiguration
                    {
                        Deck = deck,
                        LoopTag = anName,
                        Frames = frames,
                    }
                );
                chAnimations.Add(anName, animation);
                continue;

                int SafeParse(string name)
                {
                    if (!int.TryParse(name, out var i))
                    {
                        logger.LogWarning("Failed to parse {} in folder {}", name, anDirectory.FullName);
                    }

                    return i;
                }
            }
        }

        return animations;
    }

    private Dictionary<string, IDeckEntry> MaybeLoadDeck(
        ILogger logger,
        IDirectoryInfo deckRoot,
        IModDecks contentDecks,
        Dictionary<string, ISpriteEntry> spriteEntries
    )
    {
        if (!deckRoot.Exists)
        {
            logger.LogWarning("Failed to find Decks directory");
            return new();
        }

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
                if (spriteEntries.TryGetValue(path, out var spr)) return spr.Sprite;
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