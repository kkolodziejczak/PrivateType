# Vocabulary packs

Curated PrivateType vocabulary packs you can download and import yourself. PrivateType never downloads, lists, or updates packs; these files are never bundled into a release.

## Using a pack

1. Download a `.privatetype-vocabulary.json` file from this folder.
2. In PrivateType, open **Settings → Vocabulary → Import pack…** and choose the file.
3. Review every phrase, choose the language the pack applies to, and select **Import pack**. Save Settings.

The import makes a local copy. Later changes to this folder do not affect it.

## File format

A pack is a UTF-8 JSON array of phrases, each written exactly as it should appear:

```json
[
  "Kubernetes",
  "PostgreSQL"
]
```

- Phrases only: no language, strength, name, author, version, or other fields. The language is chosen at import; strength is the user's vocabulary-wide setting.
- At most 200 phrases, 120 characters each, one line each, no duplicates, 64 KB per file.
- Files here must be in canonical form: sorted ordinally, two-space indented, LF line endings, and a final newline. PrivateType's own **Export…** writes this form.

## Contributing a pack

- Only add phrases that are public knowledge (product names, technical terms, well-known names). Never add private names, dictated text, or anything taken from recordings.
- Add terms the model demonstrably gets wrong; common words gain nothing and dilute the pack.
- The repository test `CuratedVocabularyPackTests` validates every file here with the same code PrivateType uses to import packs. Run `dotnet test tests/PrivateType.Core.Tests` before opening a pull request.
