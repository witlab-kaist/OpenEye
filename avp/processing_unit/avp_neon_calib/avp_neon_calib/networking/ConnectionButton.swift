//
//  ConnectionButton.swift
//  avp_neon_calib
//
//  Created by 박강태 on 9/12/25.
//

import SwiftUI

struct ConnectionButton: View {
    @EnvironmentObject var client: TCPClient
    
    private var fillColor: Color {
        switch client.state {
        case .disconnected: return .gray.opacity(0.8)
        case .connecting:   return .yellow.opacity(0.9)
        case .connected:    return .green.opacity(0.9)
        case .failed:       return .red.opacity(0.9)
        }
    }

    private var symbolName: String {
        switch client.state {
        case .disconnected: return "bolt.horizontal.circle"
        case .connecting:   return "arrow.triangle.2.circlepath"
        case .connected:    return "checkmark.circle"
        case .failed:       return "exclamationmark.circle"
        }
    }

    private var accessibilityLabel: String {
        switch client.state {
        case .disconnected: return "TCP Disconnected. Tap to connect."
        case .connecting:   return "TCP Connecting..."
        case .connected:    return "TCP Connected. Tap to disconnect."
        case .failed:       return "TCP Failed. Tap to retry."
        }
    }

    var body: some View {
        Button {
            switch client.state {
            case .disconnected:
                Task { await MainActor.run { client.connect() } }
            case .connecting:
                break
            case .connected:
                Task { await MainActor.run { client.disconnect() } }
            case .failed:
                Task { await MainActor.run { client.retry() } }
            }
        } label: {
            ZStack {
                Circle()
                    .fill(fillColor)
                    .frame(width: 36, height: 36)
                    .shadow(radius: 4)

                Image(systemName: symbolName)
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundStyle(.white)
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(Text(accessibilityLabel))
    }
}
