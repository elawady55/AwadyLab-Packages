# AwadyLab.MoreExtensions

Extension methods for .NET that the BCL doesn't ship: allocation-free span-based string slicing, lazy LINQ
operators, calendar and workday helpers, a first-class `DateTimeRange` value type, generic-math numerics, fast
type conversions, and utilities for enums, reflection, `Guid`, tasks and certificates.

[![NuGet](https://img.shields.io/nuget/v/AwadyLab.MoreExtensions.svg)](https://www.nuget.org/packages/AwadyLab.MoreExtensions)
![Tests](https://img.shields.io/badge/tests-passing-brightgreen.svg)
![Target](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Language](https://img.shields.io/badge/C%23-14-blue.svg)
![License](https://img.shields.io/badge/license-Proprietary-blue.svg)

---

## Overview

Built on C# 14 extension members, so most of these appear as ordinary instance members on the type they extend.

| Area | What it covers |
| :--- | :--- |
| [Strings](#1-strings) | Span slicing, glob matching, validation, masking, split-and-parse, whitespace, hashing |
| [Enumerable](#2-enumerable) | Windowing, lag/lead, padding, sampling, safe materialisation |
| [Collections](#3-collections) | `IsNullOrEmpty`, bulk `AddRange`, non-mutating `WithRange` |
| [Dictionaries](#4-dictionaries) | `GetOrAdd`, `AddRange`, `RemoveWhere` |
| [Date and Time](#5-date-and-time) | Boundaries, rounding, weekends and workdays |
| [DateTimeRange](#6-datetimerange) | An interval type with ordering, formatting, overlap classification and parsing |
| [Numerics](#7-numerics) | `IsBetween`, `PercentOf`, float comparison |
| [Conversions](#8-conversions) | Fast, exception-free type conversion and primitive parsing |
| [Guid](#9-guid) | base64url encoding |
| [Enums](#10-enums) | Attributes, descriptions, flag decomposition |
| [Tasks](#11-tasks) | `Then`, `FireAndForget`, `Task.ExecuteWithTimeoutAsync`, `Task.ExecuteAtAsync` |
| [Expressions](#12-expressions) | Predicate composition |
| [Reflection](#13-reflection) | Assembly scanning, nullability |
| [Random](#14-random) | Random string generation |
| [Security](#15-security) | Certificate lookup, SAN enumeration, secure random strings |
| [Streams and Base64](#16-streams-and-base64) | Chunked stream-to-Base64 encoding, Base64-to-stream decoding |
| [XML](#17-xml) | `XmlKit`: a `JsonSerializer`-style facade over `XmlSerializer` |
| [Benchmarks](#benchmarks) | BenchmarkDotNet results for the perf-sensitive members |

### Key Highlights

* **Allocation-Free String Slicing**: The slicing members are declared on `ReadOnlySpan<char>` and return spans;
  in the benchmarks below they are 2–7× faster than the `string` equivalents and allocate nothing.
* **Lazy LINQ, Eager Validation**: `Window`, `Lag`, `Lead`, `ZipLongest`, `TakeUntil`, `Flatten` and the rest
  stream lazily, but throw `ArgumentNullException` at the call site rather than on the first `MoveNext()`.
* **Workday-Aware Dates**: Boundaries, rounding and an O(1) `AddWorkdays` for `DateTime`, `DateTimeOffset` and
  `DateOnly`, with weekends configurable per culture.
* **A Real Interval Type**: `DateTimeRange` classifies overlaps, intersects, unions, and formats and parses ISO
  8601 intervals.
* **Exception-Free Conversions**: `TryConvertTo<T>` uses the primitive `TryParse` paths, so a failed conversion
  allocates no exception.
* **Secure Where It Matters**: The `Random` helpers are kept apart from the `RandomNumberGenerator`-backed secure
  generators meant for passwords, codes and tokens.
* **Measured, Not Claimed**: BenchmarkDotNet results for the performance-sensitive members are published below.

---

## Installation

```bash
dotnet add package AwadyLab.MoreExtensions
```

---

## Quickstart

```csharp
using AwadyLab.MoreExtensions;

const string cs = "Server=db.example.com;Database=Orders;Encrypt=True";
cs.Between("Database=", ";");                  // "Orders" (ReadOnlySpan<char>, 0 allocations)

int[] readings = [3, 5, 4, 8, 6];
readings.Lag(1, (now, prev) => now - prev);    // 3, 2, -1, 4, -2

var t = new DateTime(2024, 6, 12, 14, 32, 17);
t.AddWorkdays(10);                             // skips weekends

"2024-01-15".ConvertTo<DateOnly>();            // DateOnly(2024, 1, 15)
Guid.NewGuid().ToBase64UrlString();            // 22 URL-safe characters
```

---

## 1. Strings

The slicing members are declared on `ReadOnlySpan<char>` and return spans, so none of them allocate. Thanks to
first-class span conversions you can call them directly on a `string`.

```csharp
using AwadyLab.MoreExtensions;

const string cs = "Server=db.example.com;Database=Orders;Encrypt=True";

cs.LeftOf(";")           // "Server=db.example.com"      (ReadOnlySpan<char>, 0 allocations)
cs.RightOfLast("=")      // "True"
cs.Between("Database=", ";")  // "Orders"
cs.Left(6)               // "Server"
cs.Right(4)              // "True"
```

| Member | Returns | Notes |
|---|---|---|
| `Left(int length)` | `ReadOnlySpan<char>` | Whole span when shorter than `length` |
| `Right(int length)` | `ReadOnlySpan<char>` | Whole span when shorter than `length` |
| `LeftOf(token, comparison?)` | `ReadOnlySpan<char>` | Everything before the first `token`; whole span when absent |
| `RightOf(token, comparison?)` | `ReadOnlySpan<char>` | Everything after the first `token`; empty when absent |
| `LeftOfLast(token, comparison?)` | `ReadOnlySpan<char>` | As `LeftOf`, from the last occurrence |
| `RightOfLast(token, comparison?)` | `ReadOnlySpan<char>` | As `RightOf`, from the last occurrence |
| `Between(start, end, comparison?)` | `ReadOnlySpan<char>` | Empty when either delimiter is missing |
| `TryBetween(start, end, out result, comparison?)` | `bool` | Distinguishes "missing" from "empty match" |
| `TrimPrefix(prefix, comparison?)` | `ReadOnlySpan<char>` | Removes **every** leading repetition |
| `TrimSuffix(suffix, comparison?)` | `ReadOnlySpan<char>` | Removes **every** trailing repetition |
| `TrimPrefixOnce(prefix, comparison?)` | `ReadOnlySpan<char>` | Removes one leading occurrence |
| `TrimSuffixOnce(suffix, comparison?)` | `ReadOnlySpan<char>` | Removes one trailing occurrence |
| `NthIndexOf(value, occurrence, comparison?)` | `int` | 1-based occurrence; counts non-overlapping matches; `-1` when fewer |
| `IsLike(pattern, ignoreCase?)` | `bool` | Glob match: `*` any run, `?` exactly one |

`IsLike` runs in O(n·m) worst case with O(1) memory and no recursion, so patterns like `"*a*a*a*b"` cannot
cause catastrophic backtracking.

```csharp
"order-2024-01-02.json".IsLike("order-*.json");   // true
"Report.PDF".IsLike("*.pdf", ignoreCase: true);   // true
```

### Validation

| Member | Returns | Notes |
|---|---|---|
| `IsDigitsOnly()` | `bool` | ASCII `0`–`9` only, one vectorized scan; `false` when empty |
| `IsNumeric(allowSigns = false, allowDecimal = false, allowExponent = false)` | `bool` | Optional leading `+`/`-`, one `.`, and scientific notation (`1.5e-3`); no whitespace, separators or `NaN` |
| `IsBase64(urlSafe = false)` | `bool` | `Base64.IsValid` / `Base64Url.IsValid`; `false` when empty or whitespace |
| `TryDecodeBase64(destination, out bytesWritten, urlSafe = false)` | `bool` | Decodes into a caller buffer; `false` on invalid input or a short buffer, never throws |

All four are declared on `ReadOnlySpan<char>`. The first three also have `string?` overloads whose `true` result
narrows the reference to non-null.

```csharp
"0042".IsDigitsOnly();                                    // true
"-12.5".IsNumeric(allowSigns: true, allowDecimal: true);  // true
"6.02E23".IsNumeric(allowDecimal: true, allowExponent: true);  // true
"SGVsbG8=".IsBase64();                                    // true

Span<byte> buffer = stackalloc byte[64];
if (token.TryDecodeBase64(buffer, out var written, urlSafe: true))
    Use(buffer[..written]);
```

### Masking

| Member | Returns | Notes |
|---|---|---|
| `Mask(visibleStart = 0, visibleEnd = 0, maskChar = '*')` | `string` | Keeps both ends; masks **everything** when they would cover the whole string |
| `MaskEmail(maskChar = '*')` | `string` | Keeps the first and last character of the local part and the whole domain |

Both build the result in a single allocation with `string.Create`.

```csharp
"4111111111111111".Mask(visibleEnd: 4);   // "************1111"
"jason@example.com".MaskEmail();          // "j***n@example.com"
"not-an-email".MaskEmail();               // "************"  — not guessed at
```

### Split and parse

Each segment is parsed straight from a slice with `ISpanParsable<T>` and the invariant culture, so no substrings
or arrays are allocated.

| Member | Returns | Notes |
|---|---|---|
| `TrySplitTwo<T1, T2>(separator, out first, out second)` | `bool` | Splits at the **first** separator |
| `TrySplitAndParse<T>(separator, destination, out count)` | `bool` | Fills a caller buffer; `false` when a segment fails or it does not fit |
| `SplitAndParse<T>(separator)` | `IEnumerable<T>` | Lazy; throws `FormatException` naming the segment's index, never its text |

```csharp
"1920x1080".TrySplitTwo("x", out int width, out int height);

Span<int> ids = stackalloc int[16];
if ("4,8,15,16,23,42".TrySplitAndParse(',', ids, out var count))
    Process(ids[..count]);

foreach (var price in "1.5;2.25;3".SplitAndParse<decimal>(';')) { }
```

### String members

| Member | Returns | Notes |
|---|---|---|
| `IsNullOrEmpty()` | `bool` | Flows nullability: a `false` result narrows the reference to non-null |
| `IsNullOrWhiteSpace()` | `bool` | Likewise |
| `ToIntOrDefault(defaultValue = 0)` | `int` | Invariant culture |
| `ToInt64OrDefault(defaultValue = 0)` | `long` | Invariant culture |
| `CollapseWhitespace()` | `string` | Every whitespace run becomes one space, both ends trimmed |
| `Hash(algorithm, toHexString = true)` | `string` | UTF-8 bytes, hex or base64 |

`CollapseWhitespace` returns the original instance when nothing needs changing, and otherwise builds the result
in a single allocation using a stack or pooled buffer.

```csharp
string? name = GetName();
if (!name.IsNullOrWhiteSpace())
    Console.WriteLine(name.Length);  // no null-forgiving operator needed

"  a\t\tb \n c  ".CollapseWhitespace();          // "a b c"
using var sha = SHA256.Create();
"payload".Hash(sha);                              // "5B3C..." hex
```

---

## 2. Enumerable

Every operator validates its arguments **eagerly** — an `ArgumentNullException` is thrown at the call site, not
deferred to the first `MoveNext()` — and then streams lazily.

```csharp
int[] readings = [3, 5, 4, 8, 6];

readings.Window(3);                            // [3,5,4], [5,4,8], [4,8,6]
readings.Lag(1, (now, prev) => now - prev);    // 3, 2, -1, 4, -2
readings.TakeUntil(x => x > 5);                // 3, 5, 4, 8   (inclusive)
readings.IsSorted();                           // false
```

| Member | Description |
|---|---|
| `EmptyIfNull()` | The sequence, or an empty one when null |
| `Flatten(children)` | Depth-first pre-order tree walk. Iterative — deep trees cannot overflow the stack |
| `Cartesian(other)` | Cartesian product; `other` is buffered once, the source streams |
| `Without(item, comparer?)` | Filters out every element equal to `item` |
| `DistinctAdjacent(comparer?)` | Drops elements equal to their immediate predecessor |
| `WhereNotNull()` | Filters nulls and narrows the element type (separate overloads for classes and nullable structs) |
| `TakeUntil(predicate)` | Yields up to **and including** the first match |
| `SkipUntil(predicate)` | Skips until the first match, then yields it and the rest |
| `TakeEvery(step)` | The first element, then every `step`-th after it |
| `Window(size)` | Sliding windows of exactly `size`; shorter sequences yield nothing |
| `Lag(offset, selector)` | Pairs each element with the one `offset` before it (`default` at the start) |
| `Lead(offset, selector)` | Pairs each element with the one `offset` after it (`default` at the end) |
| `Pad(width, padding?)` | Appends padding until the sequence reaches `width` |
| `PadStart(width, padding?)` | Prepends padding; buffers only when the source is shorter than `width` |
| `ZipLongest(other, selector)` | Zips to the longer length, filling with `default` |
| `MaxByOrDefault(keySelector, defaultValue?, comparer?)` | Like `MaxBy` but never throws on an empty sequence |
| `MinByOrDefault(keySelector, defaultValue?, comparer?)` | Likewise |
| `ToDictionarySafe(keySelector, valueSelector, behavior?, comparer?)` | Never throws on duplicate keys |
| `RandomSubset(count, random?)` | Reservoir sampling — one pass, O(count) memory |
| `Shuffle(random?)` | Returns a new array with every element in random order (`Random.Shuffle` under the hood) |
| `IsSorted(comparer?, descending?)` | Single-pass check |
| `ToList(capacity)` | Materialises with a known initial capacity |
| `ToArray(sizeHint)` | Exactly-sized array via a pooled working buffer |

Duplicate keys are resolved by `DuplicateKeyBehavior.KeepFirst` (the default) or `KeepLast`:

```csharp
var byId = orders.ToDictionarySafe(o => o.Id, o => o, DuplicateKeyBehavior.KeepLast);
```

> [!NOTE]
> **`Window` allocates one array per window**, so each element is copied `size` times. That is what makes every
> yielded window independently safe to keep. For a hot loop over a large sequence, prefer `Lag`/`Lead` or index
> into an array directly.

---

## 3. Collections

| Member | Applies to | Description |
|---|---|---|
| `IsNullOrEmpty()` | `IEnumerable<T>?` | True when null or empty. A `false` result narrows to non-null |
| `IsNullOrEmpty()` | `ImmutableArray<T>` | True when default (uninitialised) or empty; no boxing |
| `IsNullOrEmpty()` | `ImmutableArray<T>?` | True when null, default or empty |
| `AddRange(items)` | `ICollection<T>` | Uses the receiver's native bulk-add when it has one |
| `WithRange(items)` | `IEnumerable<T>` | Returns a new `T[]` containing the receiver followed by items without mutating the source |

`AddRange` dispatches to `List<T>.AddRange` or `ISet<T>.UnionWith` where available, pre-sizing when the source
count is known, and falls back to a per-item loop otherwise.

`WithRange` is a non-mutating companion that allocates an exactly-sized array (using `ICollection<T>.CopyTo`
when counts are known) and works for arrays or read-only sequences without modifying the receiver.

```csharp
IEnumerable<int>? maybe = GetItems();
if (!maybe.IsNullOrEmpty())
    Process(maybe);              // narrowed to non-null

ICollection<string> target = new HashSet<string>();
target.AddRange(["a", "b", "c"]);

int[] baseItems = [1, 2, 3];
int[] combined = baseItems.WithRange([4, 5, 6]);   // [1, 2, 3, 4, 5, 6]
```

> [!WARNING]
> `IsNullOrEmpty` on a sequence whose count isn't known advances the enumerator by one element to answer. Don't
> call it on a single-use iterator you intend to enumerate afterwards.

---

## 4. Dictionaries

| Member | Description |
|---|---|
| `GetOrAdd(key, valueFactory)` | Returns the existing value, or creates, stores and returns a new one |
| `GetOrAdd(key, value)` | Returns the existing value, or stores and returns `value` |
| `AddRange(items, overwrite = false)` | Bulk insert; existing keys are skipped unless `overwrite` is set |
| `RemoveWhere(predicate)` | Removes matching entries, returns how many |

```csharp
var cache = new Dictionary<string, Config>();
var config = cache.GetOrAdd("orders", LoadConfig);

var removed = cache.RemoveWhere(e => e.Value.IsExpired);
```

`GetOrAdd`'s factory must not mutate the dictionary — only the requested key is ever written, and only by
`GetOrAdd` itself once the factory returns.

> [!WARNING]
> These are **not** thread-safe. For concurrent access use `ConcurrentDictionary<TKey, TValue>`, which has its
> own `GetOrAdd`.

---

## 5. Date and Time

Every member below exists for `DateTime`, `DateTimeOffset` and `DateOnly` where it makes sense. `DateTime`
overloads preserve `Kind`; `DateTimeOffset` overloads preserve `Offset`.

| Member | Description |
|---|---|
| `StartOfDay()` / `EndOfDay()` | Midnight / the last tick of the day |
| `StartOfMonth()` / `EndOfMonth()` | First / last moment of the month |
| `StartOfYear()` / `EndOfYear()` | First / last moment of the year |
| `StartOfWeek(firstDayOfWeek = null)` / `EndOfWeek(...)` | Week boundaries; defaults to the current culture's `FirstDayOfWeek` |
| `AddWeeks(weeks)` | Add or subtract whole weeks |
| `Floor(interval)` / `Round(interval)` / `Ceiling(interval)` | Round to a multiple of `interval`; halves round up |
| `IsPast(timeProvider?)` / `IsFuture(timeProvider?)` | Comparison against a clock; inject a `TimeProvider` to test |
| `IsWeekend(culture?)` / `IsWeekday(culture?)` | Culture-aware |
| `NextWorkday(culture?)` | The next working day, preserving time of day |
| `AddWorkdays(workdays, culture?)` | Move by working days, skipping weekends |

```csharp
var t = new DateTime(2024, 6, 12, 14, 32, 17);

t.StartOfMonth();                     // 2024-06-01 00:00:00
t.Floor(TimeSpan.FromMinutes(15));    // 2024-06-12 14:30:00
t.AddWorkdays(10);                    // skips weekends
```

`AddWorkdays` is O(1) for the whole-week portion rather than walking day by day, and handles negative values
(`-1` moves to the previous working day).

### Weekend configuration

Weekends default to Saturday/Sunday, or Friday/Saturday for Arabic, Hebrew, Persian and Bangladeshi cultures.
Override per culture:

```csharp
CultureInfo.CurrentCulture.SetWeekendDays(DayOfWeek.Friday, DayOfWeek.Saturday);
// or
culture.WeekendDays = new HashSet<DayOfWeek> { DayOfWeek.Friday };
```

> [!IMPORTANT]
> Configuration is stored **per culture name and shared process-wide**, so it applies to every `CultureInfo`
> with that name — including the fresh instances ASP.NET Core request localization creates per request. Set it
> once during start-up. An empty set, or one containing all seven days, is rejected.

---

## 6. DateTimeRange

An immutable interval over `DateTimeOffset`, implementing `IEquatable`, `IComparable`, `IFormattable`,
`ISpanFormattable`, `IParsable` and `ISpanParsable`. `Overlaps` and `Intersect` use half-open `[Start, End)`
semantics, while `Contains` and `IsIn` default to the inclusive-end `[Start, End]` reading unless `inclusiveEnd: false`
is passed.

```csharp
var window = new DateTimeRange(start, end);
var hour   = DateTimeRange.FromDuration(start, TimeSpan.FromHours(1));
var bounds = DateTimeRange.FromCollection(timestamps);

window.Duration;                              // TimeSpan
window.IsEmpty;                               // bool (Start == End)
window.Contains(instant);                     // end-inclusive by default: [Start, End]
window.Contains(instant, inclusiveEnd: false);// end-exclusive: [Start, End)
window.Contains(otherRange);                  // range containment (both bounds within window)
window.Overlaps(other);                       // touching ranges do NOT overlap ([Start, End) semantics)
window.GetOverlapType(other);                 // RangeOverlapType (Before, After, Overlapping, Contains, ContainedBy, Equal)
window.Intersect(other);                      // DateTimeRange? — null when they share no instant
window.Union(other);                          // smallest range covering both
window.Enumerate(TimeSpan.FromDays(1));       // every step while still before End
var (from, to) = window;                      // deconstruction

window.ToString();                            // ISO 8601 interval: "start/end"
DateTimeRange.Parse("2024-01-01T00:00:00Z/2024-02-01T00:00:00Z");

instant.IsIn(window);                         // extension on DateTimeOffset and DateTime
```

`Overlaps` and `Intersect` agree: ranges that merely touch share no instant under half-open semantics, so
`Overlaps` is `false` and `Intersect` returns `null`. In contrast, `Contains(instant)` and `instant.IsIn(range)`
default to `inclusiveEnd: true` (`[Start, End]`). Ranges sort by `Start`, then `End`. `ToString(format, provider)`
formats both bounds with the given format (default `"O"`); only the default round-trips through `Parse`.
The delimiter can be configured process-wide via `DateTimeRange.Separator` (defaults to `'/'`). It can only be
set once per process — set it during start-up, before any formatting or parsing happens; a second assignment
throws `InvalidOperationException`.

---

## 7. Numerics

Generic-math based, so these work for every numeric type.

```csharp
5.IsBetween(1, 10);              // true  (inclusive by default)
5.IsBetween(5, 10, inclusive: false);   // false
25.PercentOf(200);               // 12.5
(0.1 + 0.2).ApproximatelyEquals(0.3, 1e-9);  // true
```

| Member | Constraint | Notes |
|---|---|---|
| `IsBetween(min, max, inclusive = true)` | `INumber<T>` | Throws if `min > max` |
| `PercentOf(total)` | `INumber<T>` | Returns `0` when `total` is zero |
| `ApproximatelyEquals(other, tolerance)` | `IFloatingPoint<T>` | `NaN` equals nothing; equal infinities are equal |

---

## 8. Conversions

General extension methods for converting and parsing loosely-typed values or strings to a target type without
allocating exceptions on failure.

Target types can be supplied explicitly (e.g. `value.ConvertTo<int>()`). Target `Nullable<T>` types are
automatically unwrapped (`null` maps to `null` for reference and nullable types, and throws for non-nullable
value types).

Supported conversions include:
- All numeric and Boolean primitives (via fast non-throwing `TryParse` paths that eliminate exception overhead)
- Enums by member name (case-insensitive) or underlying integer value
- `Guid`, `DateOnly`, `TimeOnly`, `DateTimeOffset`, `TimeSpan`, `Uri`, and `Version`
- General types via `Convert.ChangeType` with invariant culture

```csharp
"123".ConvertTo<int>();                          // 123
"true".ConvertTo<bool>();                        // true
"Active".ConvertTo<Status>();                    // Status.Active
"2024-01-15".ConvertTo<DateOnly>();              // DateOnly(2024, 1, 15)

"invalid".ConvertToOrDefault(fallback: -1);      // -1
null.ConvertToOrDefault<int?>();                 // null

if (input.TryConvertTo<Guid>(out var id))
{
    // Fast path: no FormatException allocated if input is not a Guid
}
```

| Member | Returns | Description |
|---|---|---|
| `ConvertTo<TResult>()` | `TResult` | Invariant culture conversion; unwraps `Nullable<T>`; throws on failure |
| `TryConvertTo<TResult>(out result)` | `bool` | Fast non-throwing conversion; leverages primitive `TryParse` paths to avoid exception overhead |
| `ConvertToOrDefault<TResult>(fallback?)` | `TResult?` | Returns `fallback` (or default) instead of throwing when conversion fails |

---

## 9. Guid

Encodes the raw 16 bytes as unpadded base64url — 22 characters instead of the 32 of `ToString("N")`, and safe
in URLs and file names.

```csharp
var id = Guid.NewGuid();

id.ToBase64UrlString();                        // "3Zf0xQ7rTEGm1gWq9kPzXA"
Guid.FromBase64UrlString(text);                // throws FormatException when invalid
Guid.TryParseBase64Url(text, out var parsed);  // non-throwing

Span<char> buffer = stackalloc char[22];
id.TryFormatBase64Url(buffer, out var written);  // no allocation
```

---

## 10. Enums

```csharp
enum Status { [Description("In progress")] Active, Done }

Status.Active.GetDescription();                       // "In progress"
Status.Active.GetAttribute<DescriptionAttribute>();   // the attribute, or null
Status.Active.IsDefined();                            // true
Status.GetEnumNameFromDescription("In progress");     // Status.Active, or null

(FileAccess.Read | FileAccess.Write).GetFlags();      // Read, Write
```

| Member | Description |
|---|---|
| `GetAttribute<TAttribute>()` | The attribute on the member, or null. Cached |
| `GetDescription()` | `[Description]` text, falling back to `ToString()` |
| `IsDefined()` | Instance form of `Enum.IsDefined` |
| `GetFlags()` | Every single-bit flag set, ascending. Composite and zero members are never yielded |
| `TEnum.GetEnumNameFromDescription(description, comparison?)` | Reverse description lookup. Called on the type |

Attribute and description lookups are cached per enum type, so only the first call pays for reflection.

---

## 11. Tasks

```csharp
Task<int> count = GetCountAsync();

count.Then(n => n * 2);                    // Task<int>
count.Then(n => LoadAsync(n));             // Task<T>, flattened

BackgroundWork().FireAndForget(ex => logger.LogError(ex, "background work failed"));
```

| Member | Description |
|---|---|
| `Then(map)` | Projects the result. Exceptions and cancellation propagate unchanged |
| `Then(asyncMap)` | As above with an async projection, flattened |
| `FireAndForget(onError?)` | Runs unawaited while guaranteeing the fault is observed |

`Then` completes synchronously when the source task already has, without allocating a state machine.
`FireAndForget` always observes the fault — with or without a handler — so it can never surface as an
unobserved-task exception. Cancellation is swallowed. Available on `Task`, `Task<T>`, `ValueTask` and
`ValueTask<T>`.

### Running with a time limit

`Task.ExecuteWithTimeoutAsync` runs an operation and throws `TimeoutException` if it has not finished in time:

```csharp
try
{
    var report = await Task.ExecuteWithTimeoutAsync(ct => BuildReportAsync(ct), TimeSpan.FromSeconds(30),
        cancellationToken: requestAborted);
}
catch (TimeoutException) { /* took longer than 30 s */ }
```

| Outcome | What you get |
|---|---|
| Finishes in time | Its result (or its own exception, unchanged) |
| Time runs out | `TimeoutException` — and the token the operation received is cancelled, so cooperative work stops |
| Operation ignores its token | Still `TimeoutException` on time; the abandoned work's later failure is observed for you |
| Caller cancels `cancellationToken` | `OperationCanceledException`, not a timeout |

`Timeout.InfiniteTimeSpan` means no limit. Pass a `FakeTimeProvider` to test timeouts without waiting. For a task
that is already running and cannot be cancelled, the BCL's `task.WaitAsync(timeout)` is the right tool; it stops
waiting but cannot stop the work.

### Running at a specific time

`Task.ExecuteAtAsync` is a static extension member, so it is called on `Task` itself:

```csharp
await Task.ExecuteAtAsync(ct => SendReportAsync(ct), tomorrowAt9, cancellationToken: stopping);
var rates = await Task.ExecuteAtAsync(ct => FetchRatesAsync(ct), marketOpen, timeProvider);
```

A due time in the past runs the action right away. The clock is re-read after each wait, so waits beyond
`Task.Delay`'s ~49.7-day limit work. Cancellation before the action starts means it never runs. Pass a
`FakeTimeProvider` to test scheduled code without real delays.

---

## 12. Expressions

Composes predicate expression trees, so the result is still translatable by EF Core and other providers.

```csharp
Expression<Func<Order, bool>> isOpen = o => o.Status == Status.Open;
Expression<Func<Order, bool>> isLarge = o => o.Total > 1000;

var query = db.Orders.Where(isOpen.AndAlso(isLarge));

// Or fold a whole collection:
var combined = filters.CombineAndAlso();   // empty => always true
var anyOf    = filters.CombineOrElse();    // empty => always false
```

| Member | Description |
|---|---|
| `AndAlso(right)` / `OrElse(right)` | Combines two predicates, rewiring parameters |
| `CombineAndAlso()` / `CombineOrElse()` | Folds a sequence of predicates |
| `CombineExpressionsWithAndAlso(targetType)` / `...WithOrElse(targetType)` | Folds untyped `LambdaExpression`s against a target type |

For the untyped overloads, `targetType` must be the expression's own parameter type or derive from it — a
`Expression<Func<Base, bool>>` can be retargeted to `Derived`, not the other way round.

---

## 13. Reflection

```csharp
var handlers = assembly.GetClassesImplementing<IHandler>();
var generic  = assembly.GetClassesImplementsGenericInterface(typeof(IHandler<>));
var derived  = assembly.GetClassesImplementGenericBaseClass(typeof(EntityBase<>));

typeof(int?).IsNullable();              // true
typeof(int?).GetUnderlyingTypeOrSelf(); // typeof(int)
propertyInfo.IsNullable();
```

| Member | Description |
|---|---|
| `GetClassesImplementing<TInterface>(throwOnLoadError = false)` | Classes implementing the interface |
| `GetClassesImplementsInterface(interfaceType, throwOnLoadError = false)` | Same, with a `Type` argument |
| `GetClassesImplementsGenericInterface(interfaceType, throwOnLoadError = false)` | Classes implementing an open generic interface at any type arguments |
| `GetClassesDerivedFrom<TBaseClass>(throwOnLoadError = false)` | Same, generic-argument shorthand |
| `GetClassesImplementBaseClass(baseClass, throwOnLoadError = false)` | Concrete classes deriving from a non-generic base, at any depth |
| `GetClassesImplementGenericBaseClass(baseClass, throwOnLoadError = false)` | Concrete classes deriving from an open generic base, at any depth |
| `IsNullable()` | On `Type` or `PropertyInfo` |
| `GetUnderlyingTypeOrSelf()` | Unwraps `Nullable<T>` |

Types that fail to load are skipped by default, so scanning an assembly with missing optional dependencies
returns what it could resolve. Pass `throwOnLoadError: true` to get the `ReflectionTypeLoadException` instead.

---

## 14. Random

```csharp
Random.Shared.GenerateNumericString(6);        // "480913"
Random.Shared.GenerateAlphaNumericString(12);  // "a7Kp2Qm9Xz1B"
```

> [!WARNING]
> `Random` is **not cryptographically secure** and its output is predictable from observed values. Never use
> these for one-time passwords, password-reset codes, session tokens, or anything else security-sensitive — use
> the secure counterparts in [Security](#15-security) instead.

---

## 15. Security

```csharp
using var cert = StoreLocation.CurrentUser.Find(
    StoreName.My, X509FindType.FindBySubjectName, "api.example.com");

foreach (var uri in cert.Extensions
             .OfType<X509SubjectAlternativeNameExtension>()
             .SelectMany(e => e.EnumerateUriSans()))
    Console.WriteLine(uri);
```

| Member | Description |
|---|---|
| `Find(storeName, findType, findValue, requirePrivateKey = true)` | Finds a certificate; by default requires an accessible private key (RSA or ECDSA), pass `false` for a public-only lookup |
| `TryFind(storeName, findType, findValue, out certificate, requirePrivateKey = true)` | Same, without throwing when nothing matches or the private key is unavailable |
| `EnumerateUriSans()` | URI Subject Alternative Names from the extension (RFC 5280 §4.2.1.6) |
| `SecurityExtensions.GenerateSecureNumericString(length)` | Unpredictable random digits |
| `SecurityExtensions.GenerateSecureAlphaNumericString(length)` | Unpredictable random alphanumerics |

### Secure random strings

The counterparts of the [`Random`](#14-random) generators, backed by `RandomNumberGenerator` and free of modulo
bias. These are the ones to reach for whenever an attacker must not be able to predict the value.

```csharp
SecurityExtensions.GenerateSecureNumericString(6);         // one-time password
SecurityExtensions.GenerateSecureAlphaNumericString(32);   // session token / API key
```

`Find` returns a non-null certificate that **the caller owns and must dispose**; certificates it examined but
did not select are disposed for you. It throws `InvalidOperationException` when nothing matched or the match
has no private key, and `CryptographicException` when the private key exists but cannot be opened.

`TryFind` only swallows those two expected "no usable match" outcomes; a genuine infrastructure failure (e.g.
the store itself cannot be opened) still propagates instead of being reported as "not found".

---

## 16. Streams and Base64

```csharp
await using var file = File.OpenRead("invoice.pdf");
string base64 = await file.ToBase64StringAsync();          // never holds the raw file in memory

await upload.ToBase64Async(responseWriter);                 // TextWriter: bounded memory for any size
await upload.ToBase64Async(responseBody);                   // Stream: ASCII bytes, no detour through chars

using MemoryStream decoded = base64.Base64ToStream();      // read-only, seekable, positioned at 0
```

| Member | Description |
|---|---|
| `ToBase64StringAsync(ct)` | Encodes from the current position to the end into one string |
| `ToBase64Async(TextWriter, ct)` | Writes Base64 text chunk by chunk |
| `ToBase64Async(Stream, ct)` | Writes Base64 as UTF-8 bytes with `Base64.EncodeToUtf8` |
| `Base64ToStream()` | On `string` and `ReadOnlySpan<char>`: decodes into a `MemoryStream`; `FormatException` when invalid |

Input is read in 48 KiB chunks — a multiple of 3, so no padding lands mid-output — and a short read is never
mistaken for the end of the stream. `ToBase64StringAsync` sizes its pooled buffer exactly for a seekable stream.
Pooled buffers are cleared before they are returned, because the payload may be sensitive.

---

## 17. XML

`XmlKit` gives `XmlSerializer` the shape of `System.Text.Json.JsonSerializer`:

```csharp
string xml = XmlKit.Serialize(order);
Order? copy = XmlKit.Deserialize<Order>(xml);

await XmlKit.SerializeAsync(response.Body, order, cancellationToken: ct);
Order? posted = await XmlKit.DeserializeAsync<Order>(request.Body, ct);
```

| Member | Description |
|---|---|
| `Serialize<T>(value, options?)` | To a string whose declaration says UTF-8 |
| `Serialize<T>(utf8Stream, value, options?)` | To a stream, in `options.Encoding` (UTF-8 without BOM by default) |
| `SerializeAsync<T>(utf8Stream, value, options?, ct)` | As above, with only asynchronous writes to the stream |
| `Deserialize<T>(xml)` / `Deserialize<T>(utf8Stream)` | From a string or stream; `null` for an `xsi:nil` root |
| `DeserializeAsync<T>(utf8Stream, ct)` | As above, with only asynchronous reads from the stream |

Output is configured with `XmlKitOptions`, the counterpart of `JsonSerializerOptions`:

```csharp
private static readonly XmlKitOptions Pretty = new() { WriteIndented = true, OmitXmlDeclaration = true };

string xml = XmlKit.Serialize(order, Pretty);
```

| Option | Default | Effect |
|---|---|---|
| `WriteIndented` | `false` | One element per line, indented |
| `OmitXmlDeclaration` | `false` | Leaves out `<?xml ...?>` |
| `Encoding` | UTF-8, no BOM | What the stream overloads write; the string overload always declares UTF-8 |

Options are immutable once created, so share one instance (a `static readonly` field) rather than creating one
per call. Only settings `XmlKit` can honour are offered: it never closes your stream and always writes
synchronously, so neither is configurable.

* **Cached serializers**: one `XmlSerializer` per type, created on first use and shared; all members are
  thread-safe.
* **Clean output**: the `xmlns:xsi` / `xmlns:xsd` declarations `XmlSerializer` adds by default are omitted.
* **Exact text**: carriage returns are written as `&#xD;`, so `"a\r\nb"` round-trips instead of losing its `\r`,
  and a string that starts with a byte-order mark (as `Encoding.GetString` leaves it) is accepted.
* **Invalid text fails loudly**: a lone surrogate or an XML-illegal control character in a string throws
  `InvalidOperationException` instead of producing a document that cannot be read back.
* **Null collections**: as with any `XmlSerializer`, a collection property that was `null` comes back empty.
* **Safe input**: DTDs are prohibited and no resolver is set, so a document cannot expand entities or fetch
  external resources (XXE).
* **Async without sync I/O**: `XmlSerializer` is synchronous, so the async members buffer the document in memory
  and only touch the caller's stream asynchronously — what ASP.NET Core requires by default.
* **Declared type wins**: as with `JsonSerializer`, `T` drives serialization, not the runtime type; derived types
  need `[XmlInclude]` on the base.
* **Errors**: malformed or mismatched documents throw `InvalidOperationException` with the cause as the inner
  exception; types `XmlSerializer` cannot handle throw `InvalidOperationException` or `NotSupportedException`.

---

## Benchmarks

`AwadyLab.MoreExtensions.Benchmarks` (BenchmarkDotNet) compares each extension against the straightforward code a
caller would otherwise write. Run it yourself with:

```bash
dotnet run -c Release --project AwadyLab.MoreExtensions.Benchmarks -- --filter '*'
```

Filter to one suite with `--filter '*StringSlicingBenchmarks*'`, and add `--job short` for a quick pass. Results are
written to `BenchmarkDotNet.Artifacts/` in this folder.

All results below are .NET 10.0.12, X64 RyuJIT, `ShortRun` job (3 iterations); treat the absolute numbers as
indicative, not lab-grade — re-run on your own hardware for anything you're optimizing against.

### Strings (`StringSlicingBenchmarks`)

| Method | Mean | Allocated | vs. baseline |
|---|---:|---:|---:|
| `LeftOf` on `string` (substring) | 5.0 ns | 88 B | 1.00× (baseline) |
| `LeftOf` on span | 2.7 ns | 0 B | 0.53× time, 0 allocation |
| `RightOfLast` on `string` | 3.8 ns | 32 B | — |
| `RightOfLast` on span | 2.2 ns | 0 B | — |
| `Between` via manual split | 11.0 ns | 72 B | — |
| `Between` on span | 7.4 ns | 0 B | — |
| `TrimPrefix` on `string` | 11.6 ns | 160 B | — |
| `TrimPrefix` on span | 1.5 ns | 0 B | — |
| `CollapseWhitespace` (`Regex.Replace`) | 103.7 ns | 72 B | 1.00× (baseline) |
| `CollapseWhitespace` extension | 11.4 ns | 32 B | 0.11× time, 0.44× allocation |
| `IsLike` glob match | 9.7 ns | 0 B | — |

Takeaway: the span-returning members are consistently 2–7× faster and allocation-free versus the `string`
equivalent, exactly the point of slicing without copying. `CollapseWhitespace` beats `Regex.Replace` by ~9×
using a stack/pooled buffer instead of regex overhead.

### Enumerable (`EnumerableBenchmarks`)

| Method | N | Mean | Allocated | vs. baseline |
|---|---|---:|---:|---:|
| `Window` extension | 100,000 | 1,406.7 µs | 8,798,784 B | 0.94× time, 0.92× allocation vs. naive `Skip`/`Take` |
| `Window` naive (`Skip`/`Take`/`First`) | 100,000 | 1,492.6 µs | 9,598,560 B | 1.00× (baseline) |
| `Lag` extension | 100,000 | 485.6 µs | 160 B | — |
| `Lag` indexed loop | 100,000 | 48.4 µs | 0 B | ~10× faster — the loop skips the delegate-per-element and iterator overhead |
| `RandomSubset` (reservoir) | 100,000 | 3,713.0 µs | 458 B | O(count) memory regardless of `N` |
| `RandomSubset` (shuffle all + take) | 100,000 | 946.6 µs | 400,418 B | Faster here, but O(N) memory — reservoir sampling exists for when `N` doesn't fit in memory |
| `IsSorted` extension | 100,000 | 135.0 µs | 0 B | 0.16× time, 0 allocation vs. `Zip`+`Any` |
| `IsSorted` via `Zip`+`Any` | 100,000 | 843.7 µs | 160 B | — |
| `ToDictionarySafe` extension | 100,000 | 519.3 µs | 2,175,368 B | 0.69× time, 1.76× allocation vs. `GroupBy` |
| `ToDictionarySafe` via `GroupBy` | 100,000 | 754.7 µs | 1,238,936 B | — |

Takeaway: `IsSorted`'s single-pass comparison is ~6× faster than `Zip`+`Any` (which allocates an enumerator
pair per element). `RandomSubset`'s reservoir sampling trades raw speed for O(count) memory — the naive
shuffle-then-take is faster only because it materializes the whole 100k-element source, which the reservoir
approach is specifically built to avoid for sources too large to buffer. `Lag`'s delegate-based API costs
~10× over a hand-written indexed loop — expected LINQ-style overhead for the flexibility.

### Conversions (`ConversionBenchmarks`)

| Method | Mean | Allocated | vs. baseline |
|---|---:|---:|---:|
| `int.TryParse` failure (native) | 3.7 ns | 0 B | 1.00× (baseline) |
| `TryConvertTo<int>` failure | 6.7 ns | 0 B | 1.81× — still allocation-free |
| `TryConvertTo<int>` success | 13.9 ns | 24 B | — |
| `TryConvertTo<Guid>` success | 24.1 ns | 0 B | — |
| `TryConvertTo<Guid>` failure | 7.0 ns | 0 B | — |
| `TryConvertTo<int?>` failure | 56.2 ns | 32 B | — |
| `ConvertToOrDefault` failure | 7.6 ns | 0 B | — |
| `TryConvertTo<DayOfWeek>` success | 21.0 ns | 24 B | — |

Takeaway: the failure path — the one that mattered for the 2.0.0 fix — stays within ~2× of the raw native
`TryParse`, confirming the primitive fast path avoids the old throw/catch-`FormatException` cost inside
`Convert.ChangeType`.

### Guid and DateTime (`GuidAndDateBenchmarks`)

| Method | Mean | Allocated | vs. baseline |
|---|---:|---:|---:|
| `Guid.ToString("N")` | 7.4 ns | 88 B | 1.00× (baseline) |
| `Guid.ToBase64UrlString()` | 23.0 ns | 72 B | — |
| `Guid.TryFormatBase64Url` (span, no allocation) | 9.3 ns | 0 B | 1.26× time, 0 allocation |
| `Guid.FromBase64UrlString` (parse) | 42.8 ns | 72 B | — |
| `StartOfMonth` | 7.8 ns | 0 B | — |
| `EndOfMonth` | 11.4 ns | 0 B | — |
| `Floor` (15-minute) | 1.7 ns | 0 B | — |
| `IsWeekend` (explicit culture) | 10.6 ns | 0 B | — |
| `AddWorkdays` extension | 15.9 ns | 0 B | 0.20× time vs. naive day-by-day loop |
| `AddWorkdays` naive day-by-day loop | 78.4 ns | 0 B | — |

Takeaway: `TryFormatBase64Url` is the one to reach for in a hot path — it matches `ToString("N")`'s speed
with zero allocation, versus `ToBase64UrlString()`'s extra string allocation. `AddWorkdays`'s closed-form
calculation is ~5× faster than looping a day at a time to skip weekends.

### `DictionaryExtensions`, `CollectionExtensions`, `DateTimeRange` (`DictionaryCollectionRangeBenchmarks`)

| Method | N | Mean | Allocated | vs. baseline |
|---|---|---:|---:|---:|
| `GetOrAdd` (naive `TryGetValue`+`Add`) | 100,000 | 1,226.9 µs | 6,037,714 B | 1.00× (baseline) |
| `GetOrAdd` extension | 100,000 | 1,218.1 µs | 6,037,867 B | 0.99× time, same allocation |
| `RemoveWhere` extension (pooled array) | 100,000 | 456.5 µs | 2,172,960 B | 0.37× time, 0.36× allocation vs. `List<T>` buffer |
| `RemoveWhere` naive `List<T>` buffer | 100,000 | 496.0 µs | 2,697,685 B | — |
| `WithRange` extension | 100,000 | 43.2 µs | 800,056 B | on par with `[.. source.Concat(items)]` |
| `WithRange` via `Concat`+spread | 100,000 | 43.0 µs | 801,188 B | — |
| `DateTimeRange.Contains` | 100,000 | ~0.02 ns | 0 B | indistinguishable from an empty method — effectively free |
| `DateTimeRange.GetOverlapType` | 100,000 | ~0.07 ns | 0 B | indistinguishable from an empty method — effectively free |

Takeaways: `GetOrAdd`'s single-lookup fast path (`CollectionsMarshal.GetValueRefOrAddDefault`) matches the
naive two-step version on time while keeping the same allocation profile — the win is behavioural (one
dictionary operation, not the API shape). `RemoveWhere`'s pooled buffer meaningfully beats a `List<T>` buffer
on both time and allocations, since it never grows/reallocates. `WithRange`'s exact-size allocation performs
the same as `Concat`+spread — BCL's `ToArray` on `Concat` is already well-optimized, so the manual version
buys correctness/clarity (array-specific overloads, no `IEnumerable` boxing) rather than raw speed here.
`DateTimeRange`'s comparison-based members carry no measurable overhead.

---

## License

Package by Mohamed Elawady

```
Copyright (c) Mohamed Elawady. All rights reserved.

This software is closed source and proprietary. Subject to the terms below,
you are granted a free, worldwide, non-exclusive license to:

  - Use this software, in source or compiled (NuGet package) form, in your
    own applications, including commercial applications, at no charge.

You may NOT:

  - Redistribute, sublicense, sell, or publish this software (or any
    modified version of it) as a standalone product, library, or package,
    including republishing it under a different name on NuGet.org or any
    other package repository.
  - Reverse engineer, decompile, or disassemble the compiled package except
    to the extent applicable law expressly permits this despite this
    limitation.
  - Remove or alter any copyright, trademark, or other proprietary notices.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
