//
//  AppModel.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/2/25.
//

import SwiftUI

/// Maintains app-wide state
@MainActor
@Observable
class AppModel {
    let calibrationID = "Calibration"
    enum CalibrationState {
        case closed
        case inTransition
        case open
    }
    var calibrationState = CalibrationState.closed
    
    let evaluationID = "Evaluation"
    enum EvaluationState {
        case closed
        case inTransition
        case open
    }
    var evaluationState = EvaluationState.closed
    
    let visualizationID = "Visualization"
    enum VisualizationState {
        case closed
        case inTransition
        case open
    }
    var visualizationState = VisualizationState.closed
}
