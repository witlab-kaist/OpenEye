//
//  ContentView.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/2/25.
//

import SwiftUI
import RealityKit
import RealityKitContent

struct ContentView: View {

    var body: some View {
        VStack {
            Spacer().frame(height: 40)
            Text("Vision Pro - Neon Calibration")
            ToggleCalibrationButton()
            ToggleEvaluationButton()
            ToggleVisualizationButton()
        }
        .padding()
        .overlay(alignment: .topLeading) {
            ConnectionButton()
                .padding(.top, 8)
                .padding(.leading, 8)
        }
    }
}
