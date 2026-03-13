@preconcurrency import Network
import Foundation

@inline(__always)
fileprivate func parseLengthBigEndian(_ data: Data) -> Int {
    precondition(data.count == 4)
    var value: UInt32 = 0
    data.withUnsafeBytes { rawBuf in
        if let base = rawBuf.baseAddress {
            memcpy(&value, base, 4)
        }
    }
    value = UInt32(bigEndian: value)
    return Int(value)
}

// ---------- Message Models ----------
struct BaseMessage: Codable { let type: String }

struct UpdateStepPayload: Codable { let step: Int }
struct UpdateStepMessage: Codable { let type: String; let payload: UpdateStepPayload }

struct XYPair: Codable { let x: Double; let y: Double }

struct GazeEvalPayload: Codable {
    let t: Double
    let raw: XYPair
    let filtered: XYPair
    let mapped: [String: XYPair]
}
struct GazeEvalMessage: Codable { let type: String; let payload: GazeEvalPayload }

struct EvalTargetPayload: Codable {
    let idx: Int
    let t_ms: Int
    let pos: XYPair?
    var x: Double { pos?.x ?? 0.0 }
    var y: Double { pos?.y ?? 0.0 }
}
struct EvalTargetMessage: Codable { let type: String; let payload: EvalTargetPayload }

struct GazeVisualPayload: Codable {
    let t: Double
    let x: Double
    let y: Double
}
struct GazeVisualMessage: Codable { let type: String; let payload: GazeVisualPayload }

// ---------- TCP Client ----------
@MainActor
final class TCPClient: ObservableObject {

    // Public state
    enum State: Equatable {
        case disconnected
        case connecting
        case connected
        case failed(String)
    }
    @Published var state: State = .disconnected

    @Published var step: Int = 0
    @Published var isCalibEnd: Bool = false

    @Published var ts: Double = 0
    @Published var gaze_x: Double = 0
    @Published var gaze_y: Double = 0
    @Published var target_x: Double = 0
    @Published var target_y: Double = 0

    // Private
    private var connection: NWConnection?
    private let host: NWEndpoint.Host
    private let port: NWEndpoint.Port
    private let workQueue = DispatchQueue(label: "TCPClient.queue", qos: .userInitiated)
    
    // Enter your IP address
    init(host: String = "XXX.XXX.XXX.XXX", port: UInt16 = 5051) {
        self.host = NWEndpoint.Host(host)
        self.port = NWEndpoint.Port(rawValue: port)!
    }

    deinit { connection?.cancel() }

    // MARK: - Public API
    func connect() {
        guard case .disconnected = state else { return }
        state = .connecting
        isCalibEnd = false
        print("connecting...")

        let conn = NWConnection(host: host, port: port, using: .tcp)
        self.connection = conn

        conn.stateUpdateHandler = { [weak self] newState in
            Task { @MainActor in
                guard let self else { return }
                switch newState {
                case .ready:
                    self.state = .connected
                    self.receive()
                case .failed(let err):
                    self.fail("Connection failed: \(err.localizedDescription)")
                case .waiting(let err):
                    self.fail("Waiting: \(err.localizedDescription)")
                case .cancelled:
                    self.cleanupConnection()
                    self.state = .disconnected
                case .preparing, .setup:
                    break
                @unknown default:
                    self.fail("Unknown NWConnection state")
                }
            }
        }

        conn.start(queue: workQueue)
    }

    func disconnect() {
        connection?.cancel()
        cleanupConnection()
        isCalibEnd = false
        state = .disconnected
        print("Disconnected from Mac Python")
    }

    func retry() {
        switch state {
        case .failed, .disconnected:
            connect()
        default:
            break
        }
    }

    func cleanupConnection() {
        connection = nil
    }

    private func fail(_ message: String) {
        state = .failed(message)
        connection?.cancel()
        connection = nil
        print(message)
    }

    // MARK: - Receive Loop
    func receive() {
        receiveHeader()
    }

    private func receiveHeader() {
        connection?.receive(minimumIncompleteLength: 4, maximumLength: 4) { [weak self] data, _, isComplete, error in
            guard let self else { return }

            if let error = error {
                Task { @MainActor in self.fail("Header receive error: \(error.localizedDescription)") }
                return
            }
            if isComplete {
                Task { @MainActor in self.disconnect() }
                return
            }
            guard let headerData = data, headerData.count == 4 else {
                Task { @MainActor in self.fail("Invalid header size") }
                return
            }

            let payloadLength = parseLengthBigEndian(headerData)
            if payloadLength > 0 {
                Task { @MainActor in self.receivePayload(length: payloadLength) }
            } else {
                Task { @MainActor in self.receiveHeader() }
            }
        }
    }

    private func receivePayload(length: Int) {
        connection?.receive(minimumIncompleteLength: length, maximumLength: length) { [weak self] data, _, isComplete, error in
            guard let self else { return }

            if let error = error {
                Task { @MainActor in self.fail("Payload receive error: \(error.localizedDescription)") }
                return
            }
            if isComplete {
                Task { @MainActor in self.disconnect() }
                return
            }
            guard let payloadData = data, payloadData.count == length else {
                Task { @MainActor in self.fail("Incomplete payload") }
                return
            }

            Task.detached {
                let decoder = JSONDecoder()

                guard let base = try? decoder.decode(BaseMessage.self, from: payloadData) else {
                    print("Failed to decode BaseMessage")
                    Task { @MainActor in self.receiveHeader() }
                    return
                }

                switch base.type {
                case "updateStep":
                    do {
                        let msg = try decoder.decode(UpdateStepMessage.self, from: payloadData)
                        Task { @MainActor in
                            self.step = msg.payload.step
                            // print("Received step: \(self.step)")
                            self.receiveHeader()
                        }
                    } catch {
                        print("Failed to decode updateStep: \(error)")
                        Task { @MainActor in self.receiveHeader() }
                    }

                case "calibrationEnd":
                    Task { @MainActor in
                        self.isCalibEnd = true
                        self.receiveHeader()
                    }

                case "evalTarget":
                    do {
                        let msg = try decoder.decode(EvalTargetMessage.self, from: payloadData)
                        Task { @MainActor in
                            self.target_x = msg.payload.x
                            self.target_y = msg.payload.y
                            self.receiveHeader()
                        }
                    } catch {
                        print("Failed to decode evalTarget: \(error)")
                        Task { @MainActor in self.receiveHeader() }
                    }
                 
                case "gazeVisual":
                    do {
                        let msg = try decoder.decode(GazeVisualMessage.self, from: payloadData)
                        Task { @MainActor in
                            self.gaze_x = msg.payload.x
                            self.gaze_y = msg.payload.y
                            self.receiveHeader()
                        }
                    } catch {
                        print("Failed to decode gazeVisual: \(error)")
                        Task { @MainActor in self.receiveHeader() }
                    }
                    
                default:
                    print("Unknown message type: \(base.type)")
                    Task { @MainActor in self.receiveHeader() }
                }
            }
        }
    }
}
