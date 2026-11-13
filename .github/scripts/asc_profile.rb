# frozen_string_literal: true

# Finds, or creates, the App Store provisioning profile for one bundle id and one distribution certificate through the
# App Store Connect API, and installs it where Xcode and .NET for iOS look for profiles. Used by the ios-testflight job
# in .github/workflows/mobile.yml; standard library only (the macOS runner's Ruby).
#
# Environment:
#   ASC_KEY_ID, ASC_ISSUER_ID   App Store Connect API key id and issuer id
#   ASC_KEY_PATH                path to the AuthKey_<id>.p8 private key
#   BUNDLE_ID                   e.g. com.customgolfstats.app (must already be registered as an App ID)
#   CERT_SERIAL                 hex serial number of the distribution certificate in the signing keychain
#   GITHUB_OUTPUT               optional; receives profile-name=<name> and profile-uuid=<uuid>
#
# A profile is reused when it is an ACTIVE IOS_APP_STORE profile for the bundle id that includes the certificate.
# Otherwise a new one is created, so a renewed certificate gets a fresh profile without anyone visiting the portal.
# Nothing secret is printed: only ids, names and the certificate serial.

require 'base64'
require 'json'
require 'net/http'
require 'openssl'
require 'uri'
require 'fileutils'

API = 'https://api.appstoreconnect.apple.com'

def env!(name)
  value = ENV[name].to_s
  abort("asc_profile: #{name} is not set") if value.empty?
  value
end

def b64url(data)
  Base64.urlsafe_encode64(data).delete('=')
end

# ES256 JWT for the App Store Connect API, valid for 10 minutes. ECDSA signatures come out of OpenSSL as DER; JWS wants
# the raw 64-byte r||s form.
def token(key_id, issuer_id, key_path)
  key = OpenSSL::PKey.read(File.read(key_path))
  now = Time.now.to_i
  header = { alg: 'ES256', kid: key_id, typ: 'JWT' }
  payload = { iss: issuer_id, iat: now, exp: now + 600, aud: 'appstoreconnect-v1' }
  input = "#{b64url(header.to_json)}.#{b64url(payload.to_json)}"
  der = key.sign(OpenSSL::Digest::SHA256.new, input)
  raw = OpenSSL::ASN1.decode(der).value.map { |n| n.value.to_s(2).rjust(32, "\0") }.join
  "#{input}.#{b64url(raw)}"
end

def request(method, path, jwt, body = nil)
  uri = URI("#{API}#{path}")
  req = method == :post ? Net::HTTP::Post.new(uri) : Net::HTTP::Get.new(uri)
  req['Authorization'] = "Bearer #{jwt}"
  req['Content-Type'] = 'application/json'
  req.body = body.to_json if body
  res = Net::HTTP.start(uri.host, uri.port, use_ssl: true) { |http| http.request(req) }
  unless res.is_a?(Net::HTTPSuccess)
    errors = (JSON.parse(res.body)['errors'] rescue nil) || []
    detail = errors.map { |e| "#{e['title']}: #{e['detail']}" }.join('; ')
    abort("asc_profile: #{method.upcase} #{uri.path} failed with HTTP #{res.code}. #{detail}")
  end
  JSON.parse(res.body)
end

def normalize_serial(serial)
  serial.to_s.sub(/\Aserial=/i, '').strip.upcase.sub(/\A0+/, '')
end

key_id = env!('ASC_KEY_ID')
issuer_id = env!('ASC_ISSUER_ID')
bundle_id = env!('BUNDLE_ID')
serial = normalize_serial(env!('CERT_SERIAL'))
jwt = token(key_id, issuer_id, env!('ASC_KEY_PATH'))

# The App ID (bundle id) has to exist: it is registered once by hand, together with the App Store Connect app record.
bundles = request(:get, "/v1/bundleIds?filter%5Bidentifier%5D=#{URI.encode_www_form_component(bundle_id)}&limit=200", jwt)['data']
bundle = bundles.find { |b| b['attributes']['identifier'] == bundle_id && %w[IOS UNIVERSAL].include?(b['attributes']['platform']) }
abort("asc_profile: no iOS App ID for #{bundle_id}. Register it first (docs/deploy/testflight.md, step 2).") unless bundle

# The distribution certificate the runner signs with, matched by serial number.
certs = request(:get, '/v1/certificates?filter%5BcertificateType%5D=DISTRIBUTION,IOS_DISTRIBUTION&limit=200', jwt)['data']
cert = certs.find { |c| normalize_serial(c['attributes']['serialNumber']) == serial }
abort("asc_profile: no distribution certificate with serial #{serial} in this team. Is IOS_DIST_CERT_P12 current?") unless cert

profiles = request(:get, "/v1/bundleIds/#{bundle['id']}/profiles?limit=200", jwt)['data']
candidates = profiles.select do |p|
  p['attributes']['profileType'] == 'IOS_APP_STORE' && p['attributes']['profileState'] == 'ACTIVE'
end
profile = candidates.find do |p|
  ids = request(:get, "/v1/profiles/#{p['id']}/certificates?limit=200", jwt)['data'].map { |c| c['id'] }
  ids.include?(cert['id'])
end

if profile
  puts "Reusing App Store profile '#{profile['attributes']['name']}'."
else
  name = "CI App Store #{bundle_id} #{Time.now.utc.strftime('%Y%m%d%H%M%S')}"
  body = {
    data: {
      type: 'profiles',
      attributes: { name: name, profileType: 'IOS_APP_STORE' },
      relationships: {
        bundleId: { data: { type: 'bundleIds', id: bundle['id'] } },
        certificates: { data: [{ type: 'certificates', id: cert['id'] }] }
      }
    }
  }
  profile = request(:post, '/v1/profiles', jwt, body)['data']
  puts "Created App Store profile '#{name}'."
end

content = Base64.decode64(profile['attributes']['profileContent'])
uuid = profile['attributes']['uuid']
# Xcode 16+ reads UserData; older tooling (and some .NET for iOS versions) read MobileDevice. Install into both.
[
  File.join(Dir.home, 'Library/Developer/Xcode/UserData/Provisioning Profiles'),
  File.join(Dir.home, 'Library/MobileDevice/Provisioning Profiles')
].each do |dir|
  FileUtils.mkdir_p(dir)
  File.binwrite(File.join(dir, "#{uuid}.mobileprovision"), content)
end

if ENV['GITHUB_OUTPUT'].to_s != ''
  File.open(ENV['GITHUB_OUTPUT'], 'a') do |f|
    f.puts("profile-name=#{profile['attributes']['name']}")
    f.puts("profile-uuid=#{uuid}")
  end
end
puts "Installed profile #{uuid}."
