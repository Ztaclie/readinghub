import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router'
import { ApiError, api, authApi } from '../lib/api'
import { Button, Card, ErrorText, Field, Input } from '../components/ui'

export function SetupPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const register = useMutation({
    mutationFn: authApi.register,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['me'] })
      await queryClient.invalidateQueries({ queryKey: ['setup'] })
      navigate('/')
    },
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not create the account.'),
  })

  return (
    <AuthFrame title="Create your account" subtitle="The first account closes registration. This server stays yours.">
      <AuthForm
        submitLabel="Create account"
        error={error}
        pending={register.isPending}
        extra={<Field label="Display name"><Input name="displayName" required maxLength={80} /></Field>}
        onSubmit={(values) => register.mutate({ email: values.email, password: values.password, displayName: values.displayName ?? '' })}
      />
    </AuthFrame>
  )
}

export function LoginPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const login = useMutation({
    mutationFn: authApi.login,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['me'] })
      navigate(params.get('next') || '/')
    },
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not sign in.'),
  })

  return (
    <AuthFrame title="Welcome back" subtitle="Sign in to continue reading where you left off.">
      <AuthForm
        submitLabel="Sign in"
        error={error}
        pending={login.isPending}
        onSubmit={(values) => login.mutate({ email: values.email, password: values.password })}
      />
    </AuthFrame>
  )
}

export function ExtensionConnectPage({ signedIn }: { signedIn: boolean }) {
  const [params] = useSearchParams()
  const redirect = params.get('redirect_uri') ?? ''
  const allowed = /^https:\/\/[a-p]{32}\.chromiumapp\.org\/?$/.test(redirect)
  const [error, setError] = useState('')
  const connect = useMutation({
    mutationFn: () => api<{ code: string }>('/api/v1/auth/extension/device-code', { method: 'POST' }),
    onSuccess: (result) => {
      const target = new URL(redirect)
      target.searchParams.set('code', result.code)
      window.location.assign(target.toString())
    },
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not connect the extension.'),
  })

  if (!signedIn) {
    const next = `/extension/connect?redirect_uri=${encodeURIComponent(redirect)}`
    return <Navigate to={`/login?next=${encodeURIComponent(next)}`} replace />
  }

  return (
    <AuthFrame title="Connect the extension" subtitle="ReadingHub will give this browser a device session. The page you were reading never sees the token.">
      <div className="space-y-4">
        {allowed ? <p className="text-sm text-muted-foreground">Return address: {redirect}</p> : <ErrorText>The return address is not a Chrome extension callback.</ErrorText>}
        {error ? <ErrorText>{error}</ErrorText> : null}
        <Button disabled={!allowed || connect.isPending} onClick={() => connect.mutate()}>
          {connect.isPending ? 'Connecting…' : 'Allow this browser'}
        </Button>
      </div>
    </AuthFrame>
  )
}

function AuthFrame({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <div className="grid min-h-screen place-items-center px-4 py-10">
      <Card className="w-full max-w-md p-8">
        <p className="font-serif text-3xl">ReadingHub</p>
        <h1 className="mt-6 font-serif text-2xl">{title}</h1>
        <p className="mt-2 text-sm text-muted-foreground">{subtitle}</p>
        <div className="mt-6">{children}</div>
      </Card>
    </div>
  )
}

function AuthForm({
  submitLabel,
  error,
  pending,
  extra,
  onSubmit,
}: {
  submitLabel: string
  error: string
  pending: boolean
  extra?: ReactNode
  onSubmit: (values: { email: string; password: string; displayName?: string }) => void
}) {
  const handle = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    onSubmit({
      email: String(data.get('email') ?? ''),
      password: String(data.get('password') ?? ''),
      displayName: String(data.get('displayName') ?? ''),
    })
  }

  return (
    <form className="space-y-4" onSubmit={handle}>
      {extra}
      <Field label="Email"><Input name="email" type="email" autoComplete="username" required /></Field>
      <Field label="Password"><Input name="password" type="password" autoComplete="current-password" minLength={10} required /></Field>
      {error ? <ErrorText>{error}</ErrorText> : null}
      <Button className="w-full" disabled={pending}>{pending ? 'Please wait…' : submitLabel}</Button>
      <p className="text-center text-sm text-muted-foreground">
        <Link to="/login" className="underline">Already set up? Sign in</Link>
      </p>
    </form>
  )
}
