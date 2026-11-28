# leetcode-practice

A local C# practice app: write a solution, run it against tests, keep your attempts.

## Run

```powershell
docker compose up -d --build
```

Open http://localhost:5190. Stop it with `docker compose down`. Your work stays in `work/`.

The first build downloads the .NET and Node images. After that the app works offline.

## Use

- **Run tests** (Ctrl+Enter) runs the visible tests.
- **Submit** also runs edge cases and large inputs. It needs both complexities and a filled-in trace table.
- Each test has 2 seconds, so an infinite loop shows "Time limit exceeded".
- Large inputs expose big differences such as O(n) and O(n²). They don't prove a Big-O.
- **Own tests** run your inputs and compare the result with your prediction. They never decide whether a solution passes.
- **Reviews** are due 3 and 7 days after the first solve. Opening a due problem gives you a blank editor; the old solution is kept in `attempts/`.
- `Console.WriteLine` output is shown under each test.

## Files

| Path | Contents |
|---|---|
| `work/{problem}/Solution.cs` | your current code |
| `work/{problem}/attempts/` | every submission with its results |
| `work/latest.json` | the latest run, used for reviews |
| `problems/` | descriptions, starter code and tests |
| `verify/` | reference and known-bad solutions |
| `src/` | the app |

## Add a problem

1. Add `problems/{id}.json`, copying an existing one. `compare` is `exact`, `unordered`, `unordered-groups`, or `close` (numbers within 0.00001, for averages).
   For problems that change an argument in place, set `resultArg` to its position.
2. For a large input, add a generator in `src/Problems.cs` and list it under `generated`.
3. Add `verify/{id}.reference.passed.cs`, and bad solutions named for the status they should get.
4. Check everything:

```powershell
docker compose build
docker compose run --rm verify
```

## Safety

The site listens only on this computer. Code runs in a container with no internet access, a read-only
file system, and limited memory, CPU and processes. This is meant for your own code, not for strangers'.
