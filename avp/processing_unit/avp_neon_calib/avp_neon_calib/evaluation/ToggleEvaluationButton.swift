//
//  ToggleImmersiveSpaceButton.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/2/25.
//

import SwiftUI

struct ToggleEvaluationButton: View {

    @Environment(AppModel.self) private var appModel

    @Environment(\.dismissImmersiveSpace) private var dismissEval
    @Environment(\.openImmersiveSpace) private var openEval

    var body: some View {
        Button {
            Task { @MainActor in
                switch appModel.evaluationState {
                    case .open:
                        appModel.evaluationState = .inTransition
                        await dismissEval()

                    case .closed:
                        appModel.evaluationState = .inTransition
                        switch await openEval(id: appModel.evaluationID) {
                            case .opened:
                                break

                            case .userCancelled, .error:
                                fallthrough
                            @unknown default:
                                appModel.evaluationState = .closed
                        }

                    case .inTransition:
                        break
                }
            }
        } label: {
            Text(appModel.evaluationState == .open ? "Stop Evaluation" : "Start Evaluation")
        }
        .disabled(appModel.evaluationState == .inTransition)
        .animation(.none, value: 0)
        .fontWeight(.semibold)
    }
}
