# Targeted Studio, smoothing and assisted-shapes recipe

The original preview.3 binary is downloaded from Actions run 42931395281.
Required SHA256: 4170BEFEC193F5049051D73744CFCAD6D300240CE840E791E2C469A7CECAB13C.
The application is never rebuilt. Only this test driver is compiled.

Three independent disposable GitHub-hosted Windows jobs:
- studio: visible navigation, drawing selectors/editors, keyboard selection, pause,
  draw/return, undo/redo/restore/clear/export buttons and close/reopen;
- smoothing: disabled, zero, 50 and 100 percent, endpoints, reduced jagged path length,
  retained raw points and exact restoration through the Studio button;
- assisted: line, rectangle, circle, ellipse, rotated rectangle/ellipse, restoration
  and a scribble that must remain freehand.

Studio commands are driven by native mouse/keyboard input. Reflection only locates
the existing controls and reads state/geometry for evidence. It does not invoke the
button handlers. The workspace is scrolled programmatically to reveal a target,
then the actual click is checked against every ancestor's clipping rectangle.
Opening the initial Studio window uses the existing ShowSettings bootstrap.
Drawing gestures pass through the original global hooks. All inputs are restricted
to the known target or an owned foreground Studio/dialog window.

Save-dialog filename entry uses native typing, preserving the fix established in
the preceding export recipe. Screenshots, point CSV files and logs are retained.
An actual click on Exporter PNG is covered here for the first time.

No local execution, installation, signing, release, source/binary edit, main update
or historical 195 suite. Earlier recipe files and evidence remain untouched.
The new workflow watches only this directory and its own workflow file.
Results must be taken from completed jobs and the subsequent root BILAN.
