// swift-tools-version: 6.0
// Golden fixture generator: encodes the control-protocol wire shapes with the SAME bare JSONEncoder the
// macOS server uses (ControlServer.swift:524), so the C# codec can be tested for byte parity.
// Run from windows/tools/golden:  swift run fixturegen <output-dir>
import PackageDescription

let package = Package(
    name: "fixturegen",
    platforms: [.macOS(.v14)],
    dependencies: [.package(path: "../../../agtermCore")],
    targets: [
        .executableTarget(
            name: "fixturegen",
            dependencies: [.product(name: "agtermCore", package: "agtermCore")]
        )
    ]
)
