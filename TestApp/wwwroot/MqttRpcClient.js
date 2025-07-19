import mqtt from 'https://jspm.dev/mqtt'
import { v4 as uuidv4 } from 'https://jspm.dev/uuid'

export class MqttRpcClient {
    constructor(brokerUrl, options = {}) {
        this.client = mqtt.connect(brokerUrl, options)
        this.pendingRequests = new Map()

        this.client.on('connect', () => {
            console.log('[MQTT] Connected')
        })

        this.client.on('message', (topic, message) => {
            const requestId = topic.split('/')[2]
            const pending = this.pendingRequests.get(requestId)
            if (pending) {
                this.pendingRequests.delete(requestId)
                try {
                    const data = JSON.parse(message.toString())
                    pending.resolve(data)
                } catch (err) {
                    pending.reject(err)
                }
            }
        })

        this.client.on('error', (err) => {
            console.error('[MQTT] Error:', err)
        })
    }

    async fetch(methodTopic, payload = {}, timeout = 10000) {
        const requestId = uuidv4()
        const responseTopic = `rpc/response/${requestId}`

        await new Promise((res, rej) => {
            this.client.subscribe(responseTopic, (err) => {
                if (err) rej(err)
                else res()
            })
        })

        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                this.pendingRequests.delete(requestId)
                this.client.unsubscribe(responseTopic)
                reject(new Error(`MQTT timeout: ${methodTopic}`))
            }, timeout)

            this.pendingRequests.set(requestId, {
                resolve: (data) => {
                    clearTimeout(timer)
                    this.client.unsubscribe(responseTopic)
                    resolve(data)
                },
                reject: (err) => {
                    clearTimeout(timer)
                    this.client.unsubscribe(responseTopic)
                    reject(err)
                }
            })

            const request = {
                requestId,
                method: methodTopic,
                payload,
                timestamp: Date.now()
            }

            this.client.publish(`rpc/request/${methodTopic}`, JSON.stringify(request))
        })
    }

    dispose() {
        this.client.end()
    }
}