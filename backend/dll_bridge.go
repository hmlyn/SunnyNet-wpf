//go:build cshared

package main

import (
	"encoding/json"
	"time"
)

var globalBridgeQueue = make(chan []byte, 4096)

func emitBridgeMessage(payload any) {
	if payload == nil {
		return
	}

	data, err := json.Marshal(payload)
	if err != nil {
		return
	}

	select {
	case globalBridgeQueue <- data:
	default:
		select {
		case <-globalBridgeQueue:
		default:
		}
		select {
		case globalBridgeQueue <- data:
		default:
		}
	}
}

func pollBridgeMessage(timeout time.Duration) []byte {
	if timeout <= 0 {
		select {
		case data := <-globalBridgeQueue:
			return data
		default:
			return nil
		}
	}

	timer := time.NewTimer(timeout)
	defer timer.Stop()

	select {
	case data := <-globalBridgeQueue:
		return data
	case <-timer.C:
		return nil
	}
}
