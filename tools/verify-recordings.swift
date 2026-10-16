import AVFoundation
import Foundation

guard let recordingDirectory = ProcessInfo.processInfo.environment["RECORDING_DIRECTORY"] else {
    fputs("Set RECORDING_DIRECTORY to the recorded run directory.\n", stderr)
    exit(2)
}
let directory = URL(fileURLWithPath: recordingDirectory, isDirectory: true)
let recordings = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
    .filter { $0.pathExtension == "mp4" }.sorted { $0.lastPathComponent < $1.lastPathComponent }
guard !recordings.isEmpty else { fatalError("No MP4 recordings found") }
var results: [[String: Any]] = []
var allPassed = true
for recording in recordings {
    let asset = AVURLAsset(url: recording)
    let semaphore = DispatchSemaphore(value: 0)
    var result: [String: Any] = ["file": recording.lastPathComponent]
    asset.loadValuesAsynchronously(forKeys: ["tracks", "duration", "playable"]) {
        do {
            let duration = CMTimeGetSeconds(asset.duration)
            guard asset.isPlayable, duration.isFinite, duration > 0,
                  !asset.tracks(withMediaType: .video).isEmpty else {
                throw NSError(domain: "FleetEvidence", code: 1,
                              userInfo: [NSLocalizedDescriptionKey: "No playable video track or positive duration"])
            }
            let generator = AVAssetImageGenerator(asset: asset)
            generator.appliesPreferredTrackTransform = true
            var dimensions: [[String: Int]] = []
            for seconds in [min(1, duration / 4), duration / 2] {
                let image = try generator.copyCGImage(at: CMTime(seconds: seconds, preferredTimescale: 600), actualTime: nil)
                dimensions.append(["width": image.width, "height": image.height])
            }
            result["result"] = "passed"
            result["durationSeconds"] = duration
            result["decodedFrames"] = dimensions
        } catch {
            result["result"] = "failed"
            result["error"] = error.localizedDescription
        }
        semaphore.signal()
    }
    if semaphore.wait(timeout: .now() + 60) == .timedOut {
        fatalError("Media verification timed out for \(recording.lastPathComponent)")
    }
    if result["result"] as? String != "passed" { allPassed = false }
    results.append(result)
}
let report: [String: Any] = ["validator": "macOS AVFoundation", "recordedAt": ISO8601DateFormatter().string(from: Date()),
                            "result": allPassed ? "passed" : "failed", "recordings": results]
let data = try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys])
try data.write(to: directory.appendingPathComponent("media-validation.json"), options: .atomic)
print(String(decoding: data, as: UTF8.self))
exit(allPassed ? 0 : 1)