# Worked answers

Why each expected answer in `tasks.jsonl` is right, and what each task is meant to catch. Code tasks are checked by their tests, and every reference answer passes them.

## Code

| Id                   | What the tests catch                                                                                       |
| -------------------- | ---------------------------------------------------------------------------------------------------------- |
| `code-brackets`      | Wrong bracket type, wrong order (`([)]`), unclosed and unopened brackets, other characters ignored         |
| `code-roman`         | Every subtractive pair, 3999                                                                               |
| `code-intervals`     | Unsorted input, touching intervals, an interval inside another, duplicates, negative numbers, empty input  |
| `code-top-words`     | Case folding, ties sorted alphabetically, apostrophes and digits splitting words, `é` splitting `café`, k = 0 |
| `code-duration`      | Units out of order or repeated, spaces, uppercase, unknown units, a number without a unit, leading zeros  |
| `code-spiral`        | Square, wide, tall, single-row, single-column and empty matrices                                           |
| `code-rpn`           | Operand order for `-` and `/`, negative numbers, truncation toward zero, a result that overflows `int`     |
| `code-edit-distance` | Empty strings, case, the classic pairs (kitten/sitting = 3, intention/execution = 5)                       |
| `code-versions`      | Numeric, not string, comparison (`1.2` < `1.10`), missing parts as 0, leading zeros, exactly -1/0/1        |
| `code-calculator`    | Precedence, left associativity, unary minus before numbers and parentheses, truncation, `long` results     |

## Extraction

**`ext-meeting`.** The meeting moved to Thursday 7 May 2026, so the date is `2026-05-07`, not the 5th. 2:30 pm to 3:15 pm is `14:30` to `15:15`. The room is `B2.14`; `A1.02` was the old room. Jonas is still an attendee even though he leaves early, and Elena is the sender.

**`ext-shipping`.** The order has 2 parcels, although this notice is for one of them. 1,250 g + 2,350 g = 3,600 g = `3.6` kg. The tracking number keeps its leading zero, so it has to be a string. The estimated delivery is `2026-06-05`; 3 June is the ship date.

**`ext-signature`.** The name is without `Dr.` and the job title is without `Platform Team`. The mobile is `+49 171 555 0199` with the spaces removed: `+491715550199`. The office number with `(0)` is the distractor.

**`ext-logs`.** There are 4 ERROR lines (billing, search, billing, auth) and 1 WARN line. The first ERROR is at `2026-09-02T08:14:09Z`. In order of first error, the services are billing, search, auth, with billing only once.

**`ext-recipe`.** It serves 4; 6 is the alternative. 20 min + 1 h 15 min = 95 min. The list has 7 lines, and "Salt and pepper to taste" counts as one. 200 °C is conventional; 180 °C is the fan temperature.

**`ext-statement`.** The IBAN without spaces is `GB33BUKB20201555555555`. There are 5 transactions. Credits are 2,450.00 + 12.99 = 2,462.99. The closing balance is consistent with the lines: 1,204.50 − 64.20 + 2,450.00 − 950.00 + 12.99 − 71.35 = 2,581.94.

**`ext-lamp`.** All three colors, including Warm White, which is sold out but still listed. 14 x 42 x 18 cm is 140 x 420 x 180 mm. The previous model's size is the distractor. Power is 9 W.

**`ext-job`.** The title is `Backend Developer` without `(C#/.NET)`. The posting gives no start date, so `start_date` is `null`; 30 January 2027 is the application deadline.

## Arithmetic

**`math-train`.** 09:47 to 12:47 is 180 minutes, and 12:47 to 13:12 is 25 more: **205**.

**`math-price`.** 80 × 1.25 = 100, × 0.80 = 80, × 1.10 = **88**. The trap is thinking +25% and −20% leave a net +5%.

**`math-interest`.** 1.03⁴ = 1.12550881. 5000 × 1.12550881 = 5627.54405, so the interest is 627.54405, which rounds to **627.54**. Simple interest would give 600.

**`math-tank`.** Per hour: 1/6 + 1/4 − 1/12 = 2/12 + 3/12 − 1/12 = 4/12 = 1/3 of the tank, so **3** hours.

**`math-multiples`.** Multiples of 3 up to 1000: 3 × (333 × 334 / 2) = 166,833. Of 5: 5 × (200 × 201 / 2) = 100,500. Of 15: 15 × (66 × 67 / 2) = 33,165. "3 or 5 but not both" removes the multiples of 15 twice: 166,833 + 100,500 − 2 × 33,165 = **201,003**. A model that subtracts them only once gets 234,168.

**`math-fuel`.** The return trip is 680 km. 680 × 6.5 / 100 = 44.2 litres, and 44.2 × 1.84 = 81.328, which rounds to **81.33**. A model that forgets the return gets 40.66 (40.664 rounded).

## Logic

**`logic-weekday`.** The day after tomorrow is Saturday, so today is Thursday and yesterday was **Wednesday**.

**`logic-knights`.** If C were a knight, A and B would both be knaves. But A's "B is a knave" would then be false, so B would be a knight, which is a contradiction. So C is a knave, and B's "C is a knave" is true, so B is a knight. A's "B is a knave" is false, so A is a knave. That's consistent with C's statement being false. Only **B** is a knight.

**`logic-race`.** Tomas, Quinn, Rosa and Sven can't be first: Tomas is ruled out directly, and the others all finish after someone. So Petra is first. Then Tomas, Quinn, Rosa and Sven fill places 2 to 5 in that order, with Sven right after Rosa. Fourth is **Rosa**. "Petra didn't finish last" is a red herring.

**`logic-sevens`.** 7, 17, …, 97 is 10 numbers with a 7 in the units place, and 70 to 79 is 10 with a 7 in the tens place. 77 is in both: 10 + 10 − 1 = **19**. Checked by counting.

**`logic-club`.** The 10th meeting is 9 intervals of 9 days after the first: 81 days. 81 = 11 × 7 + 4, so it's 4 weekdays after Monday: **Friday**.

**`logic-houses`.** Green and blue are adjacent (clue 1), and red and yellow are not (clue 2), so green and blue are houses 2 and 3, and red and yellow are 1 and 4. Clue 3 puts yellow in 1 and red in 4. Ada is in blue, house 3. Bo is at an end (clue 5) but not in red (clue 8), so Bo is in house 1. Cy isn't next to Bo, so Cy is in house 4 and Di in house 2, next to Ada (clue 7). Red house: **Cy**. A brute-force check over every arrangement finds only this one.
