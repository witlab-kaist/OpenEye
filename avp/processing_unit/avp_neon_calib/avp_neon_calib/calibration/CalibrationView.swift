//
//  ImmersiveView.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/2/25.
//

import SwiftUI
import RealityKit
import RealityKitContent

struct CalibrationView: View {
    
    @Environment(\.dismissImmersiveSpace) private var dismissCalib
    @EnvironmentObject var client: TCPClient
    
    var body: some View {
        RealityView { content in
                        
            let headAnchor = AnchorEntity(.head)
            
            let container = Entity()
            container.position = [0, 0, -1]
            
            let radius: Float = 0.01
            let distance: Float = 1.0
            let horizontalAngles: [Float] = [-15, -7.5, 0, 7.5, 15]
            let verticalAngles: [Float] = [10, 5, 0, -5, -10]
            
            var dots: [ModelEntity] = []
            for vAngleDeg in verticalAngles {
                for hAngleDeg in horizontalAngles {
                    
                    let hAngleRad = hAngleDeg * .pi / 180
                    let vAngleRad = vAngleDeg * .pi / 180
                    let x = distance * tan(hAngleRad)
                    let y = distance * tan(vAngleRad)
                    
                    let dot = ModelEntity(
                        mesh: .generateSphere(radius: radius),
                        materials: [SimpleMaterial(color: .cyan, isMetallic: false)]
                    )
                    
                    dot.position = [x, y, 0]
                    dot.isEnabled = false
                    container.addChild(dot)
                    dots.append(dot)
                }
            }
            
            headAnchor.addChild(container)
            content.add(headAnchor)
            
            Task {
                var lastStep = -1
                
                while true {
                    try? await Task.sleep(nanoseconds: 100_000_000) // 0.1sec wait
                    
                    if client.isCalibEnd {
                        await dismissCalib()
                        break
                    }

                    let currentStep = client.step
                    if currentStep != lastStep {
                        if lastStep >= 0 && lastStep < dots.count {
                            dots[lastStep].isEnabled = false
                        }
                        if currentStep >= 0 && currentStep < dots.count {
                            dots[currentStep].isEnabled = true
                        }
                        lastStep = currentStep
                    }
                }
            }
        }.onAppear {
            client.isCalibEnd = false
            client.step = 0
        }
    }
}
