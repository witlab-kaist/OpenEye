//
//  GazeImmersiveView.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/18/25.
//

import SwiftUI
import RealityKit
import RealityKitContent

struct VisualizationView: View {
    @Environment(\.dismissImmersiveSpace) private var dismissVisualization
    @EnvironmentObject var client: TCPClient

    private let dotRadius: Float = 0.015

    var body: some View {
        RealityView { content in
            let headAnchor = AnchorEntity(.head)

            let container = Entity()
            container.position = [0, 0, -1.0]

            let gazeDot = ModelEntity(
                mesh: .generateSphere(radius: dotRadius),
                materials: [SimpleMaterial(color: .white.withAlphaComponent(0.85), isMetallic: false)]
            )
            gazeDot.position = [0, 0, 0]
            container.addChild(gazeDot)

            headAnchor.addChild(container)
            content.add(headAnchor)

            Task { @MainActor in
                applyGazePosition(dot: gazeDot)
            }

            _ = content.subscribe(to: SceneEvents.Update.self) { _ in
                Task { @MainActor in
                    applyGazePosition(dot: gazeDot)
                }
            }
        }
    }

    @MainActor
    private func applyGazePosition(dot: ModelEntity) {
        let gx = Float(client.gaze_x)
        let gy = Float(client.gaze_y)
        dot.position = [gx, gy, 0]
    }
}
