using System.Globalization;
using System.Reflection;
using System.Text;
using CobaltCoreEasyLoader.Data;
using CsvHelper;
using HarmonyLib;
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
    public OneOf<Yes, No, Error<string>> CanLoadPlugin(IPluginPackage<IModManifest> package)
    {
        return package.Manifest.ModType == "EasyLoader" ? pluginLoader.CanLoadPlugin(package) : new No();
    }

    public PluginLoadResult<Mod> LoadPlugin(IPluginPackage<IModManifest> package)
    {
        var root = package.PackageRoot;
        var logger = modLoggerGetter(package);
        var helper = modHelperGetter(package);
        var content = helper.Content;

        var localization = MaybeLoadLocalization(logger, root.GetRelativeFile("Data/Localization.csv"));
        var missingLocalization = new List<string>();
        var usedLocalization = new HashSet<string>();

        SingleLocalizationProvider? RequestLocalization(string key)
        {
            var notSeen = usedLocalization!.Add(key);
            if (localization!.TryGetValue(key, out var provider))
            {
                return provider;
            }

            if (notSeen)
            {
                missingLocalization.Add(key);
            }

            return null;
        }


        var sprites = MaybeLoadSprites(
            logger,
            root.GetRelativeDirectory("Sprites"),
            content.Sprites
        );
        var decks = MaybeLoadDeck(
            logger,
            root.GetRelativeDirectory("Data/Decks"),
            content.Decks,
            RequestLocalization,
            sprites
        );
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
            var modCards = content!.Cards;
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
                var typeName = (type.FullName ?? type.Name).ToLower();
                var name = $"cards/{type.Name.ToLower()}";
                sprites.TryGetValue(name, out var art);
                if (art == null)
                {
                    logger.LogWarning($"Failed to find sprite {name}");
                }

                var card = modCards.RegisterCard(
                    typeName,
                    new CardConfiguration
                    {
                        CardType = type,
                        Meta = cardMeta,
                        Art = art?.Sprite,
                        Name = RequestLocalization(name),
                    }
                );
                registeredCards.Add(card);
            }

            return registeredCards;
        }

        List<IArtifactEntry> RegisterArtifacts(string deckName, List<Type> artifacts)
        {
            var registeredArtifacts = new List<IArtifactEntry>();
            var deck = GetDeck(deckName);
            var modArtifacts = content!.Artifacts;
            foreach (var type in artifacts)
            {
                var artifactMeta = type.GetCustomAttribute<ArtifactMeta>() ??
                               new ArtifactMeta
                               {
                                   unremovable = false,
                                   dontLoc = false,
                                   extraGlossary = [],
                                   pools = [ArtifactPool.Common],
                               };
                artifactMeta.owner = deck;

                var typeName = (type.FullName ?? type.Name).ToLower();
                var name = $"artifacts/{type.Name.ToLower()}";
                sprites.TryGetValue(name, out var art);
                if (art == null)
                {
                    logger.LogWarning($"Failed to find sprite {name}");
                }

                var artifact = modArtifacts.RegisterArtifact(
                    typeName,
                    new ArtifactConfiguration()
                    {
                        ArtifactType = type,
                        Name = RequestLocalization(name + "_name"),
                        Description = RequestLocalization(name + "_desc"),
                        Meta = artifactMeta,
                        Sprite = art?.Sprite ?? Spr.artifacts_Unknown,
                    }
                );
                registeredArtifacts.Add(artifact);
            }

            return registeredArtifacts;
        }

        ICharacterEntry RegisterCharacter(
            string deckName,
            List<Type> starterCard,
            List<Type> starterArtifacts,
            bool startLocked
        )
        {
            var deck = GetDeck(deckName);

            var character = content.Characters.RegisterCharacter(
                deckName,
                new CharacterConfiguration
                {
                    Deck = deck,
                    Starters = new StarterDeck
                    {
                        artifacts = starterArtifacts.Select(type => type.Constructor().Invoke(null) as Artifact).Where(cond => cond != null).Select(cond => cond!).ToList(),
                        cards = starterCard.Select(type => type.Constructor().Invoke(null) as Card).Where(cond => cond != null).Select(cond => cond!).ToList(),
                    },
                    StartLocked = startLocked,
                    BorderSprite = TryGetSprite("panel") ?? Spr.panels_char_colorless,
                    Description = RequestLocalization($"{deckName}/desc"),
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
                logger.LogWarning("Failed to find sprite {}", path);

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
            InjectParam(RegisterArtifacts),
            InjectParam(RegisterCharacter),
            InjectParam(RegisterCharacter2),
            InjectParam(RequestLocalization)
        );

        parameterInjector.RegisterParameterInjector(injector);
        try
        {
            return pluginLoader.LoadPlugin(package);
        }
        finally
        {
            parameterInjector.UnregisterParameterInjector(injector);
            FinalizeLocalization();
        }

        void FinalizeLocalization()
        {
            var sb = new StringBuilder();
            if (usedLocalization.Count != (localization.Count + missingLocalization.Count))
            {
                var count = 0;
                foreach (var key in localization.Keys.Where(key => usedLocalization.Contains(key)))
                {
                    count++;
                    sb.AppendLine(key);
                }

                sb.Insert(0, $"Found {count} unused localization lines\n");
                logger.LogWarning("{}", sb.ToString());
                sb.Clear();
            }

            if (missingLocalization.Count == 0) return;
            sb.AppendLine($"Found {missingLocalization.Count} missing localization entries");
            sb.AppendJoin('\n', missingLocalization);
            logger.LogWarning("{}", sb.ToString());
        }
    }

    private Dictionary<string, SingleLocalizationProvider> MaybeLoadLocalization(ILogger logger, IFileInfo fileInfo)
    {
        if (!fileInfo.Exists)
        {
            logger.LogWarning("Failed to find {}", fileInfo.FullName);
            return new();
        }

        var localization = new Dictionary<string, SingleLocalizationProvider>();

        using var streamReader = new StreamReader(fileInfo.OpenRead());
        using var csvParser = new CsvParser(streamReader, CultureInfo.InvariantCulture);

        if (!csvParser.Read())
        {
            logger.LogWarning("Localiation file at {} was empty!", fileInfo.FullName);
            return new Dictionary<string, SingleLocalizationProvider>();
        }

        var headers = csvParser.Record!.ToList();
        while (csvParser.Read())
        {
            var dict = headers.Zip(csvParser.Record!).ToDictionary();
            if (dict.Count == 0) continue;
            if (!dict.TryGetValue("key", out var key))
            {
                logger.LogWarning("Localization file at {} is missing key column", fileInfo.FullName);
                continue;
            }

            localization.Add(key, dict.GetValueOrDefault);
        }

        return localization;
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
            // logger.LogWarning("Failed to find Sprites/Characters directory");
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
        Func<string, SingleLocalizationProvider?> requestLocalization,
        Dictionary<string, ISpriteEntry> spriteEntries
    )
    {
        if (!deckRoot.Exists)
        {
            // logger.LogWarning("Failed to find Decks directory");
            return new();
        }

        var decks = new Dictionary<string, IDeckEntry>();

        foreach (var fileInfo in deckRoot.Files)
        {
            var ext = Path.GetExtension(fileInfo.Name);
            if (ext != ".json" && ext != ".toml")
            {
                logger.LogWarning("Found non json/toml file in Sprites directory: {}", fileInfo.FullName);
                continue;
            }

            var name = fileInfo.Name[..^5].ToLower();
            var easyDeckDef = ext == ".toml" ? TomlParse<EasyDeckDef>(fileInfo) : JsonParse<EasyDeckDef>(fileInfo);

            var borderSprite = TryGetSprite("bordersprite", true) ?? Spr.cardShared_border_colorless;
            var defaultCardArt = TryGetSprite($"defaultcardart", true) ?? Spr.cards_colorless;
            var overBorderSprite = TryGetSprite("overbordersprite");

            var deck = contentDecks.RegisterDeck(
                name,
                new DeckConfiguration
                {
                    Definition = easyDeckDef,
                    BorderSprite = borderSprite,
                    DefaultCardArt = defaultCardArt,
                    Name = requestLocalization($"{name}/name"),
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
            // logger.LogWarning("Failed to find Sprites directory");
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

    private T JsonParse<T>(IFileInfo info, bool full = false)
    {
        using var reader = new StreamReader(info.OpenRead());
        var text = reader.ReadToEnd();
        if (!full)
        {
            return JsonConvert.DeserializeObject<T>(text)!;
        }

        return JsonConvert.DeserializeObject<T>(
            text,
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
            }
        )!;
    }
}