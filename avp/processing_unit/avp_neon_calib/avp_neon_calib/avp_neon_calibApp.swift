//
//  avp_neon_calibApp.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/2/25.
//

import SwiftUI

@main
struct avp_neon_calibApp: App {

    @State private var appModel = AppModel()
    @StateObject private var tcpClient = TCPClient()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(appModel)
                .environmentObject(tcpClient)
        }
        .defaultSize(width: 400, height: 400)
        
        ImmersiveSpace(id: appModel.calibrationID) {
            CalibrationView()
                .environment(appModel)
                .environmentObject(tcpClient)
                .onAppear {
                    appModel.calibrationState = .open
                }
                .onDisappear {
                    appModel.calibrationState = .closed
                }
        }
        .immersionStyle(selection: .constant(.mixed), in: .mixed)
        
        ImmersiveSpace(id: appModel.evaluationID) {
            EvaluationView()
                .environment(appModel)
                .environmentObject(tcpClient)
                .onAppear {
                    appModel.evaluationState = .open
                }
                .onDisappear {
                    appModel.evaluationState = .closed
                }
        }
        .immersionStyle(selection: .constant(.mixed), in: .mixed)
        
        ImmersiveSpace(id: appModel.visualizationID) {
            VisualizationView()
                .environment(appModel)
                .environmentObject(tcpClient)
                .onAppear {
                    appModel.visualizationState = .open
                }
                .onDisappear {
                    appModel.visualizationState = .closed
                }
        }
        .immersionStyle(selection: .constant(.mixed), in: .mixed)
    }
}
