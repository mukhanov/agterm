// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "agtermCore",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "agtermCore", targets: ["agtermCore"]),
        .executable(name: "agtermctl", targets: ["agtermctl"]),
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-argument-parser", from: "1.3.0"),
        .package(url: "https://github.com/dduan/TOMLDecoder", from: "0.4.5"),
    ],
    targets: [
        .target(name: "agtermCore", dependencies: [.product(name: "TOMLDecoder", package: "TOMLDecoder")]),
        .testTarget(name: "agtermCoreTests", dependencies: ["agtermCore"]),
        .target(
            name: "agtermctlKit",
            dependencies: [
                "agtermCore",
                .product(name: "ArgumentParser", package: "swift-argument-parser"),
            ]
        ),
        .executableTarget(name: "agtermctl", dependencies: ["agtermctlKit"]),
        .testTarget(name: "agtermctlKitTests", dependencies: ["agtermctlKit"]),
    ],
    swiftLanguageModes: [.v6]
)

#if os(macOS)
// AgtermResponsibility wraps posix_spawn responsibility attrs (Darwin-only); nothing outside these
// session-host targets needs it, and it already fails to build on Linux — gated so a Windows
// `swift build`/`swift test` runs without it.
package.products.append(.library(name: "AgtermResponsibility", targets: ["AgtermResponsibility"]))
package.products.append(.executable(name: "agterm-session-host", targets: ["agterm-session-host"]))
package.targets += [
    .target(name: "AgtermResponsibility"),
    .target(name: "SessionHostTrampoline"),
    .target(name: "SessionHostRuntime", dependencies: ["SessionHostTrampoline", "AgtermResponsibility", "agtermCore"]),
    .executableTarget(name: "agterm-session-host", dependencies: ["SessionHostRuntime"]),
    // The fixtures need a socket client they can copy into a test bundle, which /usr/bin/nc
    // cannot be (see the target's own comment).
    .executableTarget(name: "session-host-test-client"),
    .executableTarget(name: "session-host-pty-probe"),
    .testTarget(name: "SessionHostRuntimeTests",
                dependencies: ["SessionHostRuntime", "agterm-session-host", "session-host-test-client",
                               "session-host-pty-probe"]),
]
#endif
