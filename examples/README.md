# Example save

`SavedData_ww_Memoria_0_0.dat` is a real save from the very start of the
game (everyone at level 1, ~12 minutes played), in the
[Memoria mod](https://github.com/Albeoris/Memoria) format — donated so
there's something to open right away without needing your own save file
first, and so the `memoria` format path is exercised by anyone trying the
project fresh. It has no personal or identifying data in it.

Try it with either front-end from a source checkout:

```bash
dotnet run --project src/FFIX.SaveEditor.Cli -- examples/SavedData_ww_Memoria_0_0.dat --inspect
dotnet run --project src/FFIX.SaveEditor.Cli -- examples/SavedData_ww_Memoria_0_0.dat --interactive
dotnet run --project src/FFIX.SaveEditor.Gui -- examples/SavedData_ww_Memoria_0_0.dat
```

`--inspect` prints:

```
format: Memoria mod save (unencrypted)
slot: Save data  gil=500  playtime=0.2h
#  name       lvl          hp        mp  str  spd  mag  spr
0  Zidane       1     105/105     36/36   21   23   18   23
1  ??????       1       60/60     48/48   12   16   24   19
2  Dagger       1       70/70     46/46   14   21   23   17
...
```

As always, `--out`/"Write New File" never touch the input, so it's safe to
experiment on directly.
