# Task set

Short tasks with answers that can be checked automatically, to see whether a model configuration still does useful work. They're asked of `llama-server` in the pinned llama.cpp image, and every reply is scored right or wrong. The score of a configuration is the share of tasks it gets right.

| File                     | Contents                                         |
| ------------------------ | ------------------------------------------------ |
| `tasks.jsonl`            | The task set: one task per line                  |
| `answers.md`             | Why each expected answer is right                |
| `examples.jsonl`         | One example task per checker, in the same format |
| `code/<id>.tests.cs`     | Tests for a code task                            |
| `code/<id>.reference.cs` | A correct answer to a code task                  |

## Format

`tasks.jsonl` is JSON Lines: one JSON object per line, UTF-8, no blank lines. A line break inside a prompt is written as `\n`.

| Field       | Type                    | Meaning                                                                          |
| ----------- | ----------------------- | -------------------------------------------------------------------------------- |
| `id`        | string                  | Unique. Lowercase letters, digits and `-`. It also names the code files.         |
| `category`  | string                  | What the task tests: `code`, `extraction`, `arithmetic` or `logic`               |
| `checker`   | string                  | How the reply is scored: `csharp-tests`, `json`, `number` or `exact`. See below. |
| `prompt`    | string                  | The task, as the user message. The runner adds the reply format for the checker. |
| `expected`  | depends on the checker  | The right answer. See below.                                                     |
| `reference` | string, code tasks only | Path of a correct answer, relative to `tasks/`                                  |

`category` and `checker` are separate so a category can be scored in more than one way, for example a logic puzzle with a numeric answer.

## Checkers

Before any checker looks at a reply, a `<think>…</think>` block is removed from it, in case the model thinks even with thinking off.

| Checker        | `expected`                                   | Reply format the runner asks for                              | Right when                                                       |
| -------------- | -------------------------------------------- | ------------------------------------------------------------- | ---------------------------------------------------------------- |
| `csharp-tests` | path of the tests file, relative to `tasks/` | one `csharp` code block with a `public static class Solution` | the reply and the tests build together and the tests exit with 0 |
| `json`         | the expected JSON object                     | only the JSON object, in one `json` code block                | the reply's JSON equals `expected`                               |
| `number`       | a number                                     | a last line `Answer: <number>`                                | the number equals `expected`                                     |
| `exact`        | a string, or a list of accepted strings      | a last line `Answer: <answer>`                                | the answer equals `expected`, or one of the list                 |

**`csharp-tests`.** The code is the first code block in the reply. It goes into a small console project next to the tests:

- `Solution.cs`: the reply's code. It holds `public static class Solution` with the method the prompt asks for, and no namespace, `Main` or top-level statements. Its own `using` directives are fine.
- `Program.cs`: the tests file. Top-level statements that call `Solution`, print each failed check, and return `0` only when every check passed.
- The project targets .NET 10 with implicit usings on and nullable warnings off. It has no package references, so it builds without a network.

A reply that doesn't build fails, the same as one that fails a test. Each code task has a `reference` answer so the tests themselves are checked: the reference has to pass them.

**`json`.** The JSON is the first code block in the reply, or the whole reply if there's no code block. Comparison is by value: key order doesn't matter, array order does, `328.8` equals `328.80`, and strings must match exactly. A missing key, an extra key or a `null` where a value is expected is wrong. Ask in the prompt for every key, with its type, and for `null` when a value isn't there.

**`number`.** The answer is on the last line that starts with `Answer:`, or `**Answer:`. Spaces, a trailing `.` and `**` around it are ignored. It must be a plain number with `.` as the decimal point: no units, currency signs or thousands separators. It's compared as a decimal, so `36.9` equals `36.90`.

**`exact`.** The answer is on the last line that starts with `Answer:`, or `**Answer:`. Spaces, a trailing `.`, and quotes, backticks or `**` around it are ignored, and the comparison ignores case. Keep these answers to a word or a short name, and list the other spellings you'd accept.

## Prompt template

Each task is one chat request with two messages.

System:

```
You are a careful assistant. Follow the reply format exactly.
```

User: the task's `prompt`, a blank line, then the format line for its checker:

| Checker        | Format line                                                                                                                                                                                       |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `csharp-tests` | `Reply with one csharp code block that contains a public static class Solution with the method above. No namespace, no Main method, no top-level statements. Use only the .NET standard library.` |
| `json`         | `Reply with only the JSON object, in one json code block.`                                                                                                                                        |
| `number`       | `You can show your working. End with a last line of the form "Answer: <number>", with the number only: no units, currency signs or thousands separators.`                                         |
| `exact`        | `You can show your working. End with a last line of the form "Answer: <answer>".`                                                                                                                 |

The chat template is the one built into each GGUF.

## Generation settings

The same for every task and every configuration, so a score changes only because the model or its configuration changed.

| Setting       | Value           | Where                                                                |
| ------------- | --------------- | -------------------------------------------------------------------- |
| Temperature   | `0`             | `--temp 0` on the server, and `temperature` in each request          |
| Max tokens    | 1024            | `max_tokens` in each request                                         |
| Thinking      | off             | `--reasoning off` on the server                                      |
| Seed          | 42              | `--seed 42`; it doesn't matter at temperature 0                      |
| Server slots  | 1               | `-np 1`, so every request is processed the same way                  |
| Prompt cache  | off             | `--no-cache-prompt`, so a reply doesn't depend on the task before it |
| Chat template | the model's own | the one built into the GGUF; no `--chat-template`                    |

Temperature 0 is greedy decoding: the most likely token every time, so a configuration gives the same replies on every run. Thinking is off because it makes replies much longer and slower, and the score would then depend on the token budget more than on the quantisation. A reply cut off at 1024 tokens is scored as it is, which usually means wrong.

The server also runs with `-fit off`, so the configuration is used exactly as given: if it doesn't fit, the server fails to start instead of quietly moving layers off the GPU. `--reasoning-format none` leaves anything the model thinks in the reply, where the checkers remove `<think>` blocks.

The flags are checked against `docker/help/b12030/llama-server.txt`.

## Sandbox

Model-written code never runs on the host. Each `csharp-tests` reply is built and run in a throwaway container from the official .NET 10 SDK image (`mcr.microsoft.com/dotnet/sdk:10.0`), pinned by digest in `docker/images.md`:

- no network (`--network none`)
- 1 GB of memory, 2 CPUs and at most 256 processes
- a read-only root file system; the project is mounted read-only and copied into a 512 MB `/tmp` to build
- 60 seconds for the build and the tests together; after that the container is killed and the task fails
- removed after each task, so nothing carries over from one reply to the next

A task takes about 2.5 seconds. `tools/tasks.cs` runs the checkers; see `tools/README.md`.

## Examples

`examples.jsonl` has one task per checker. They're examples of the format, not part of the task set.

**Code with tests** (`ex-code`). The tests are in `code/ex-code.tests.cs` and the reference answer in `code/ex-code.reference.cs`. The tests cover the empty string, case, runs of 10 or more and a character that comes back after another run.

```json
{
    "id": "ex-code",
    "category": "code",
    "checker": "csharp-tests",
    "prompt": "Write a C# method `public static string Compress(string s)` that run-length encodes a string: each run of the same character becomes that character followed by the length of the run. Characters are case-sensitive. For example, \"aaabcc\" becomes \"a3b1c2\", and \"\" becomes \"\".",
    "expected": "code/ex-code.tests.cs",
    "reference": "code/ex-code.reference.cs"
}
```

**JSON extraction** (`ex-json`). The invoice has distractors: a due date next to the issue date, and a subtotal before the total. It has no PO number, so the right answer is `null`.

```json
{
    "id": "ex-json",
    "category": "extraction",
    "checker": "json",
    "prompt": "Extract these fields from the invoice below as a JSON object:\n- invoice_number (string)\n- issue_date (string, YYYY-MM-DD)\n- total (number)\n- currency (string, ISO 4217 code)\n- line_items (integer, the number of line items)\n- po_number (string, or null if the invoice has none)\n\nInvoice:\nNorthwind Supplies Ltd\nInvoice no. NW-2041\nIssued 14 March 2026, due 13 April 2026\n\n2 x Desk lamp @ 34.50 = 69.00\n1 x Office chair @ 189.00 = 189.00\n5 x Notebook A5 @ 3.20 = 16.00\n\nSubtotal: 274.00\nVAT 20%: 54.80\nTotal due: €328.80",
    "expected": {
        "invoice_number": "NW-2041",
        "issue_date": "2026-03-14",
        "total": 328.8,
        "currency": "EUR",
        "line_items": 3,
        "po_number": null
    }
}
```

**Arithmetic** (`ex-number`). Two boxes and 6 single pens cost 28.80 + 8.10 = 36.90. One box and 18 pens cost 38.70, and 30 single pens cost 40.50. Three boxes would be cheaper per pen, but that's 36 pens, not exactly 30.

```json
{
    "id": "ex-number",
    "category": "arithmetic",
    "checker": "number",
    "prompt": "Pens cost 1.35 each, or 14.40 for a box of 12. Boxes can't be split. What is the lowest price for exactly 30 pens?",
    "expected": 36.9
}
```

**Logic** (`ex-exact`). Ben owns neither the dog nor the fish, so he owns the cat. Anna doesn't own the dog, so Cara does, and Anna owns the fish.

```json
{
    "id": "ex-exact",
    "category": "logic",
    "checker": "exact",
    "prompt": "Anna, Ben and Cara each own exactly one pet: a cat, a dog or a fish, and no two of them own the same kind. Ben owns neither the dog nor the fish. Anna doesn't own the dog. Who owns the fish? Give the name only.",
    "expected": "Anna"
}
```

## Writing tasks

The set has 30 tasks: 10 code, 8 extraction, 6 arithmetic and 6 logic. They were drafted with a model, and every expected answer was checked by hand against `answers.md`.

- Every task needs exactly one right answer. If a careful person could argue for another, reword the task, or list both answers for `exact`.
- Put everything the task needs in the prompt. The model has no files, tools or internet.
- Check each expected answer by working the task out, and write the working in `answers.md`.
- For code, test the edge cases as well as the example in the prompt, and make sure the reference answer passes.
- Make some tasks hard. If every configuration scores 100 %, the set can't tell them apart.
