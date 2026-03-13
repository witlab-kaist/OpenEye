//
//  ImmersiveView.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/2/25.
//

import SwiftUI
import RealityKit
import RealityKitContent

struct EvaluationView: View {
    @Environment(\.dismissImmersiveSpace) private var dismissEval
    @EnvironmentObject var client: TCPClient

    var body: some View {
        RealityView { content in
            let headAnchor = AnchorEntity(.head)
            headAnchor.name = "evalAnchor"
            
            let dot = ModelEntity(
                mesh: .generateSphere(radius: 0.01),
                materials: [SimpleMaterial(color: .cyan, isMetallic: false)]
            )
            dot.name = "evalDot"
            dot.position = [0, 0, -1]
            
            headAnchor.addChild(dot)
            content.add(headAnchor)
            
        } update: { content in
            guard
                let anchor = content.entities.first(where: { $0.name == "evalAnchor" }) as? AnchorEntity,
                let dot = anchor.findEntity(named: "evalDot")
            else { return }
            
            // TCP coord
            let x = Float(client.target_x)
            let y = Float(client.target_y)
            
            if x.isFinite && y.isFinite {
                dot.position = SIMD3<Float>(x, y, -1)
                dot.isEnabled = true
            } else {
                dot.isEnabled = false
            }
        }
    }
}
