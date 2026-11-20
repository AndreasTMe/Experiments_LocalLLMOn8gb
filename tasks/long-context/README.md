# Long-context facts

Made-up facts to plant in a long text, and the question that asks about each one. `tools/long-context.cs` turns them into a tasks file that `tools/tasks.cs` runs and scores; see `tools/README.md`.

`facts.jsonl` has one fact per line:

| Field      | Meaning                                                        |
| ---------- | -------------------------------------------------------------- |
| `id`       | The task id                                                    |
| `position` | Where the fact goes, as a share of the text's length (0 to 1)  |
| `fact`     | The sentence that's put in the text                            |
| `question` | The question the model is asked, after the text                |
| `checker`  | `number` or `exact`, as in `tasks/README.md`                   |
| `expected` | The right answer, in the form the checker needs                |

| Id        | Position | Answer   |
| --------- | -------: | -------- |
| `fact-a`  |      3 % | 4817     |
| `fact-b`  |      6 % | QX-7342  |
| `fact-c`  |      9 % | quince   |
| `fact-d`  |    9.5 % | Thursday |
| `control` |     50 % | 268      |

The first four are early in the text, far from the question. The control sits in the middle: if a model misses it too, the problem isn't only distance.

Every name and number in the facts was checked against `wiki.test.raw` from WikiText-2, the text they're planted in, and none of them appears there. So the answer can only come from the planted sentence, not from the text around it or from what the model already knows.

The text is the start of `wiki.test.raw`. It isn't in the repo; it goes in `staging/wikitext-2-raw/`, and the generated tasks files in `staging/long-context/`.
