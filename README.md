Xmas Tree to 3D
----

Some C# code inspired by harvards code [here](https://github.com/GSD6338/XmasTree).

**Usage:**

1. Use XmasTreeLightsTo3DCaptureImages to capture images. There's a hardcoded IP address which should point to a WLED instance. It expects all LEDs to be on a single segment (others will be ignored). Update the direction (8 directions) each time, then run the program.
2. Run Process3DImages. You can fiddle about with a few constants at the top to tweak your results if needed.
3. Run TestTreeCoordinates to run 3 sweeping planes along each axis, then flash the LEDs' binary.

**Future plans:** (being worked on now)

1. Implement Matt's 'correction' algorithm.
2. See if we can do something with the Y values particularly to discount certain angles where the Y is way off (obviously wrong). Run the equation solver again, discounting those to see if we get a better (smaller equation delta) coordinates.
3. Possibly bundle all programs into a web interface (probably using Blazor because why not).
4. Find out why after controling WLED's leds individually WLED ignores the web UI commands (to fix reboot WLED).
