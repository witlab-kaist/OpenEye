//
//  VisualizationButton.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/18/25.
//

import SwiftUI

struct ToggleVisualizationButton: View {

    @Environment(AppModel.self) private var appModel

    @Environment(\.dismissImmersiveSpace) private var dismissVisualization
    @Environment(\.openImmersiveSpace) private var openVisualization

    var body: some View {
        Button {
            Task { @MainActor in
                switch appModel.visualizationState {
                    case .open:
                        appModel.visualizationState = .inTransition
                        await dismissVisualization()

                    case .closed:
                        appModel.visualizationState = .inTransition
                        switch await openVisualization(id: appModel.visualizationID) {
                            case .opened:
                                break

                            case .userCancelled, .error:
                                fallthrough
                            @unknown default:
                                appModel.visualizationState = .closed
                        }

                    case .inTransition:
                        break
                }
            }
        } label: {
            Text(appModel.visualizationState == .open ? "Turn Off" : "Visualization")
        }
        .disabled(appModel.visualizationState == .inTransition)
        .animation(.none, value: 0)
        .fontWeight(.semibold)
    }
}
