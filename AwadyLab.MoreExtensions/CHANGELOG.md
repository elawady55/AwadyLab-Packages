# Changelog

All notable changes to `AwadyLab.MoreExtensions` are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses [Semantic Versioning](https://semver.org/).

## [1.6.0] - 2026-10-03

Everything in this release is new API; nothing existing changed behaviour, so upgrading from 1.5.x is safe.

### Added

#### Strings — validation

- `IsDigitsOnly()` on `ReadOnlySpan<char>` and `string?`: non-empty and ASCII `0`–`9` only, checked in one
  vectorized scan. Non-ASCII digits such as `'٣'` or full-width `'１'` are rejected.
- `IsNumeric(allowSigns, allowDecimal, allowExponent)` on `ReadOnlySpan<char>` and `string?`: a plain
  invariant-culture number with an optional leading sign, one `.` and scientific notation (`"-1.5e-3"`). Each part
  is opt-in; whitespace, group separators, `NaN` and `Infinity` are always rejected.
- `IsBase64(urlSafe)` on `ReadOnlySpan<char>` and `string?`: valid, non-empty Base64 or Base64Url, using the
  vectorized `Base64.IsValid` / `Base64Url.IsValid`. Empty and whitespace-only text returns `false`.
- `TryDecodeBase64(destination, out bytesWritten, urlSafe)` on `ReadOnlySpan<char>`: decodes into a caller buffer
  without allocating; returns `false` (never throws) on invalid input or a buffer that is too small.
- The `string?` overloads carry `[NotNullWhen(true)]`, so a `true` result narrows the reference to non-null.

#### Strings — masking

- `Mask(visibleStart, visibleEnd, maskChar)`: keeps the requested ends and masks the rest, in a single allocation.
  When the visible ends would cover the whole string, every character is masked instead of revealing the value.
- `MaskEmail(maskChar)`: `"jason@example.com"` → `"j***n@example.com"`. The domain is kept, the last `@` splits
  (quoted local parts may contain `@`), and anything not shaped like an address is masked completely.

#### Strings — split and parse

Each segment is parsed straight from a slice with `ISpanParsable<T>` and the invariant culture, so no substrings or
arrays are allocated.

- `TrySplitTwo<T1, T2>(separator, out first, out second)`: splits at the first separator and parses both halves.
- `TrySplitAndParse<T>(separator, destination, out count)`: fills a caller buffer; `false` when a segment does not
  parse or the buffer is too small, with `count` reporting how many values were written.
- `SplitAndParse<T>(separator)`: a lazy `IEnumerable<T>`. A bad segment throws `FormatException` naming its index,
  never its text, which may be sensitive.

#### Streams and Base64 (`StreamExtensions`)

- `ToBase64StringAsync(ct)`: encodes a stream from its current position in 48 KiB chunks, without ever holding the
  raw content in memory. Seekable streams get an exactly sized buffer.
- `ToBase64Async(TextWriter, ct)` and `ToBase64Async(Stream, ct)`: write Base64 text, or ASCII bytes via
  `Base64.EncodeToUtf8`, with memory bounded by two pooled buffers whatever the payload size.
- `Base64ToStream()` on `string` and `ReadOnlySpan<char>`: decodes into a read-only, seekable `MemoryStream`.
- Short reads are never mistaken for the end of the stream, and pooled buffers are cleared before they are returned.

#### Tasks

- `Task.ExecuteWithTimeoutAsync(action, timeout, timeProvider, ct)` (with and without a result): runs an operation
  and throws `TimeoutException` if it has not finished in time. The token the operation receives is cancelled so
  cooperative work stops; work that ignores it still times out on schedule, and its later failure is observed.
  Caller cancellation stays an `OperationCanceledException`, so the two can be told apart.
- `Task.ExecuteAtAsync(action, executeAt, timeProvider, ct)` (with and without a result): runs an operation at a
  given time. Waits beyond `Task.Delay`'s ~49.7-day limit are supported, and cancellation before the due time means
  the operation never runs.
- Both are static extension members called on `Task` itself, and both accept a `TimeProvider`, so tests can use
  `FakeTimeProvider` instead of real delays.

#### XML (`XmlKit`, `XmlKitOptions`)

- `XmlKit`: a `JsonSerializer`-style facade over `XmlSerializer` — `Serialize` / `Deserialize` for strings and
  streams, plus `SerializeAsync` / `DeserializeAsync`.
- `XmlKitOptions`: the counterpart of `JsonSerializerOptions`, with `WriteIndented`, `OmitXmlDeclaration` and
  `Encoding`. Immutable once created, so one instance can be shared across threads.
- One cached `XmlSerializer` per type; every member is thread-safe.
- Safe input: DTDs are prohibited and no resolver is set, so documents cannot expand entities or fetch external
  resources (XXE, billion laughs).
- Exact text: carriage returns are written as `&#xD;`, so `"a\r\nb"` round-trips instead of losing its `\r`, and a
  string starting with a byte-order mark is accepted.
- Clean output: no `xmlns:xsi` / `xmlns:xsd` clutter on the root element.
- Async members only touch the caller's stream asynchronously, as ASP.NET Core requires by default; the caller's
  stream is never closed.
- Invalid text (lone surrogates, XML-illegal control characters) throws `InvalidOperationException` instead of
  producing a document that cannot be read back.

### Notes

- The public API is now tracked with `Microsoft.CodeAnalysis.PublicApiAnalyzers`; an undeclared change to the
  public surface fails the build.

[1.6.0]: https://www.nuget.org/packages/AwadyLab.MoreExtensions/1.6.0
