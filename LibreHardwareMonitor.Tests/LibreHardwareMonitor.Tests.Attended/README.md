# LibreHardwareMonitor.Tests.Attended

Boundary suite for hardware-dependent and attended tests: anything that needs
real sensors, elevated hardware access, or a person driving the UI lives here.

The suite is part of `LibreHardwareMonitor.sln`, but it is deliberately absent
from `LibreHardwareMonitor.Tests.slnf` and from every configured gate command,
so it sits outside deterministic CI by construction. Adding this suite (or any
of its tests) to a deterministic gate requires a new accepted specification.

It currently holds no tests.
