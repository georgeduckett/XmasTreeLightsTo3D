Xmas Tree to 3D
----

Some C# code inspired by harvards code [here](https://github.com/GSD6338/XmasTree).

**Running the app:**

This can be ran as a web app hosted in something like IIS, or within Docker.

Here's an example `compose.yaml`:

```
services:
  treelights:
    build:
      context: https://github.com/georgeduckett/xmastreelightsto3d.git#main
      dockerfile: TreeLightsWeb/Dockerfile
    pull_policy: build
    container_name: treelights
    ports:
      - 8080:8080
      - 8081:8081
    volumes:
      - /opt/dockerdata/dataprotectionkeys:/root/.aspnet/DataProtection-Keys
      - /opt/dockerdata/treelights/config:/app/wwwroot/Config
      - /opt/dockerdata/treelights/capturedimages:/app/wwwroot/CapturedImages
    environment:
      TreeURIBase: http://192.168.0.70
    restart: unless-stopped
```
I've not yet published it as a proper docker image so it's getting it straight from github and building the solution.

**How to use**

The program connects to a WLED instance at the environmental variable `TreeURIBase`. It expects all LEDs to be one long segment in WLED. The WLED controller needs a fairly reliable wifi connection (or ethernet) otherwise the animations will stall, as will the image capture routine.

You then need to capture all the tree images. This is by far the most time-consuming bit, and the one you want to get right. Make sure the camera can see the whole tree (at least all LEDs). Try to remove / reduce any reflections. When processessing the images you can blank the left and right of the images to help with some of that, but I don't have much to deal with things like reflecting off the floor, or maybe a back wall. Best is to have the tree not right up against a wall if possible, with maybe something non-reflective under it, ideally. You'll need to run the image capture routine once per rotation, for 8 rotations. When rotating make sure the whole tree rotates as some trees have the main 'trunk' able to rotate free of the base so if you rotate the base a branch could get stuck and not be properly rotated; you could always put something on one of the branches (non reflective though!) to ensure it's rotated properly. Also make sure you're keeping track of which angle you're doing / have done! If you find an LED gets stuck on without the routine capturing more images chances are the web app has lost connection to the tree. It will keep retrying so I find a reboot of the WLED power does the trick.

The next step is to process the images. There are various controls you can tweak; I've used what seems to work best for me, and probably for you too. One key aspect is that as part of the procesing we need to solve linear systems. We can do that either using a C implementation, or using the Math.NET library. I have found that the C library seems a little better, in that fewer LED locations need correcting however it only works when running on windows as the NuGet package [HSG.Numerics](https://www.nuget.org/packages/HSG.Numerics) only provides that. What you can do is run the app somewhere else (debug mode is fine) just to process the images, then host it in docker; the C library is only used during the procesing of the images. Just make sure to copy the captured images from where they were captured to the instance where you're running the C library (it expects them in `wwwroot/CapturedImages`).

Once that's done the tree is usable and the animations on the homepage should work. You can use the Coordinate correction page (WIP) to view the calculated coordinates of each LED and their associated images. You can use the `Tree` page to see the tree coordinates, with the points lighting up according to the currently playing animation.

**Known Issues**

After controling WLED's leds individually (by this app) WLED ignores it's own web UI commands (to fix reboot WLED, either physically or using it's web UI).
