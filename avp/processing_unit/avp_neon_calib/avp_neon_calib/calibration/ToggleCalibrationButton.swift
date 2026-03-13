//
//  ToggleImmersiveSpaceButton.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/2/25.
//

import SwiftUI

struct ToggleCalibrationButton: View {

    @Environment(AppModel.self) private var appModel

    @Environment(\.dismissImmersiveSpace) private var dismissCalib
    @Environment(\.openImmersiveSpace) private var openCalib

    var body: some View {
        Button {
            Task { @MainActor in
                switch appModel.calibrationState {
                    case .open:
                        appModel.calibrationState = .inTransition
                        await dismissCalib()

                    case .closed:
                        appModel.calibrationState = .inTransition
                        switch await openCalib(id: appModel.calibrationID) {
                            case .opened:
                                break

                            case .userCancelled, .error:
                                fallthrough
                            @unknown default:
                                appModel.calibrationState = .closed
                        }

                    case .inTransition:
                        break
                }
            }
        } label: {
            Text(appModel.calibrationState == .open ? "Stop Calibration" : "Start Calibration")
        }
        .disabled(appModel.calibrationState == .inTransition)
        .animation(.none, value: 0)
        .fontWeight(.semibold)
    }
}
